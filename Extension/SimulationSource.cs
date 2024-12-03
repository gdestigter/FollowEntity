using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.RealTime;
using Esri.ArcGISRuntime.Tasks.NetworkAnalysis;

namespace FollowEntity;

[ObservableObject]
public partial class SimulationSource : DynamicEntityDataSource
{
    public const string EntityIdField = "Id";
    public const string HeadingField = "Heading";
    public const string StatusField = "Status";

    private readonly Envelope _extent;
    private readonly RouteTask _routeTask;
    private readonly FeatureTable _warehouseTable;
    private List<Feature>? _warehouseFeatures;
    private readonly Timer _observationTimer;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly List<ActiveRoute> _routes = [];
    private readonly int _trucksPerWarehouse = 2;
    private int _observationInterval = 200;

    public SimulationSource(Envelope extent, RouteTask routeTask, FeatureTable warehouseTable)
    {
        _extent = extent;
        _routeTask = routeTask;
        _warehouseTable = warehouseTable;
        _observationTimer = new(TimerCallback, null, Timeout.Infinite, Timeout.Infinite);
        PropertyChanged += SimulationSource_PropertyChanged;
    }

    #region Properties

    [ObservableProperty]
    public int _speedAdjustment = 5;

    #endregion

    protected override async Task<DynamicEntityDataSourceInfo> OnLoadAsync()
    {
        // load and verify the warehouse table
        await _warehouseTable.LoadAsync();
        if (_warehouseTable.NumberOfFeatures <= 0)
            throw new InvalidOperationException($"Warehouse table is empty");

        // forward the schema / entity Id / metadata to the API
        return new DynamicEntityDataSourceInfo(EntityIdField, GetSchema()) { SpatialReference = SpatialReferences.Wgs84 };
    }

    protected override async Task OnConnectAsync(CancellationToken cancellationToken)
    {
        // create initial reoutes and start the simulation
        await CreateInitialRoutesAsync();
        _observationTimer.Change(100, _observationInterval);
    }

    protected override Task OnDisconnectAsync()
    {
        // stop the simulation
        _observationTimer.Change(Timeout.Infinite, Timeout.Infinite);
        return Task.CompletedTask;
    }

    private async void TimerCallback(object? o)
    {
        try
        {
            // only run the method if the previous run is complete
            if (!_semaphore.Wait(0))
            {
                Trace.WriteLine($"{DateTime.Now} | Skipped generate frame");
                return;
            }

            // update current routes (delete routes or start new routes if necessary)
            var routeList = _routes.ToList();
            foreach (var route in routeList)
            {
                AddObservation(route.CalculateNextPoint(1), CreateRouteAttributes(route));

                if (route.RouteStatus is RouteStatus.Complete)
                {
                    // back at the warehouse, delete the entity and start a new route
                    _ = DeleteEntityAsync(route.RouteId);
                    _routes.Remove(route);
                    await StartNewRouteAsync(route.EndPoint);
                }
                else if (route.RouteStatus is RouteStatus.AtDestination)
                {
                    // at the destination, reset the route to return to the warehouse
                    var networkRoute = await SolveRouteAsync(route.EndPoint, route.StartPoint);
                    route.ResetRoutePath((Polyline)networkRoute.RouteGeometry!, networkRoute.TotalLength / networkRoute.TotalTime.TotalSeconds);
                }
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"Error generating observations: {ex}");
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private void SimulationSource_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpeedAdjustment))
        {
            _observationInterval = 1000 / SpeedAdjustment;
            if (ConnectionStatus == ConnectionStatus.Connected)
                _observationTimer.Change(100, _observationInterval);
        }
    }

    private async Task CreateInitialRoutesAsync()
    {
        _routes.Clear();

        _warehouseFeatures = [.. (await _warehouseTable.QueryFeaturesAsync(new() { WhereClause = "1=1" }))];
        foreach (var warehouseFeature in _warehouseFeatures)
        {
            if (warehouseFeature.Geometry is not MapPoint warehousePoint)
                continue;

            for (int n = 0; n < _trucksPerWarehouse; ++n)
            {
                try
                {
                    await StartNewRouteAsync(warehousePoint, true);
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex.ToString());
                }
            }
        }
    }

    private async Task StartNewRouteAsync(MapPoint startPoint, bool isEnRoute = false)
    {
        var endPoint = GetRandomMapPointWithinExtent(_extent);
        var route = await SolveRouteAsync(startPoint, endPoint);
        if (route.RouteGeometry is not Polyline routePath)
            throw new InvalidOperationException("Route path could not be found");

        var metersPerSecond = route.TotalLength / route.TotalTime.TotalSeconds;
        var activeRoute = new ActiveRoute(routePath, metersPerSecond);
        if (isEnRoute)
            activeRoute.SecondsTraveled = Random.Shared.NextDouble() * route.TotalTime.TotalSeconds;
        _routes.Add(activeRoute);
    }

    private async Task<Route> SolveRouteAsync(MapPoint startPoint, MapPoint endPoint)
    {
        var routeParams = await _routeTask.CreateDefaultParametersAsync();
        routeParams.OutputSpatialReference = SpatialReferences.WebMercator;
        routeParams.SetStops([new(startPoint), new(endPoint)]);
        var routeResult = await _routeTask.SolveRouteAsync(routeParams);
        return routeResult.Routes[0];
    }

    private static MapPoint GetRandomMapPointWithinExtent(Envelope extent)
    {
        return new MapPoint(
            extent.XMin + Random.Shared.NextDouble() * (extent.XMax - extent.XMin),
            extent.YMin + Random.Shared.NextDouble() * (extent.YMax - extent.YMin),
            extent.SpatialReference);
    }

    private List<KeyValuePair<string, object?>> CreateRouteAttributes(ActiveRoute route)
    {
        return
        [
            KeyValuePair.Create<string, object?>(EntityIdField, route.RouteId),
            KeyValuePair.Create<string, object?>(HeadingField, route.CurrentHeading),
            KeyValuePair.Create<string, object?>(StatusField, (int)route.RouteStatus),
        ];
    }

    private List<Field> GetSchema()
    {
        return
        [
            new Field(FieldType.Text, EntityIdField, EntityIdField.ToUpper(), 256),
            new Field(FieldType.Float64, HeadingField, HeadingField.ToUpper(), 8),
            new Field(FieldType.Int32, StatusField, StatusField.ToUpper(), 4),
        ];
    }
}
