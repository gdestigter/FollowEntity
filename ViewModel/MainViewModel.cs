using System.Drawing;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.RealTime;
using Esri.ArcGISRuntime.Symbology;
using Esri.ArcGISRuntime.Tasks.NetworkAnalysis;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;

namespace FollowEntity.ViewModel;

public enum FollowMode
{
    Manual,
    LocationDataSource,
    CameraController
}

public partial class MainViewModel : ObservableRecipient
{
    private const string MobileMapPackagePath = @"Content\SanDiegoNetwork.mmpk";
    private const string WarehouseGeodatabasePath = @"Content\start_points.geodatabase";
    private const string ElevationUrl = "https://elevation3d.arcgis.com/arcgis/rest/services/WorldElevation3D/Terrain3D/ImageServer";

    private static readonly Envelope _extent = new(
        -13053376.10252461, 3851361.7018923508, -13029715.044618936, 3863009.455819231,
        SpatialReferences.WebMercator);

    private UniqueValueRenderer? _renderer;
    private GeodatabaseFeatureTable? _warehouseTable;
    private FeatureLayer? _warehouseLayer;
    private RouteTask? _routeTask;
    private DynamicEntityLayer? _dynamicEntityLayer;

    public MainViewModel()
    {
        _ = InitializeAsync();
    }

    #region Properties

    public GeoModel GeoModel
    {
        get => _geoModel;
        private set
        {
            SetProperty(ref _geoModel, value);
            OnPropertyChanged(nameof(IsSceneView));
        }
    }
    private GeoModel _geoModel = new Map(BasemapStyle.ArcGISStreets) { InitialViewpoint = new Viewpoint(_extent) };

    public bool IsSceneView => GeoModel is Scene;

    public FollowEntityController FollowEntityController { get; } = new();

    [ObservableProperty]
    private FollowMode _followMode = FollowMode.Manual;

    [ObservableProperty]
    private SimulationSource? _simulationSource;

    [ObservableProperty]
    private DynamicEntity? _selectedEntity;

    #endregion

    private async Task InitializeAsync()
    {
        try
        {
            // load the mobile map package and route task
            var mmpk = await MobileMapPackage.OpenAsync(MobileMapPackagePath);
            _routeTask = await RouteTask.CreateAsync(mmpk.Maps[0].TransportationNetworks[0])
                 ?? throw new InvalidOperationException("Invalid route task");

            // warehouse points
            var warehouseGeodatabase = await Geodatabase.OpenAsync(WarehouseGeodatabasePath);
            _warehouseTable = warehouseGeodatabase.GetGeodatabaseFeatureTable("StartPoints")
                ?? throw new InvalidOperationException("Could not load warehouse points table");
            _warehouseLayer = new FeatureLayer(_warehouseTable);
            GeoModel.OperationalLayers.Add(_warehouseLayer);

            // truck renderer
            _renderer = Renderer.FromJson(File.ReadAllText(@"Content\Renderer.json")) as UniqueValueRenderer;

            // simulation entities
            SimulationSource = new SimulationSource(_extent, _routeTask, _warehouseTable);
            _dynamicEntityLayer = new DynamicEntityLayer(SimulationSource);
            _dynamicEntityLayer.TrackDisplayProperties.ShowPreviousObservations = true;
            _dynamicEntityLayer.TrackDisplayProperties.ShowTrackLine = true;
            _dynamicEntityLayer.TrackDisplayProperties.MaximumObservations = 50;
            _dynamicEntityLayer.Renderer = _renderer;
            GeoModel.OperationalLayers.Add(_dynamicEntityLayer);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Initialization Error");
        }
    }

    [RelayCommand]
    private async Task GeoViewTappedAsync(GeoViewInputEventArgs eventArgs)
    {
        if (_dynamicEntityLayer is null)
            return;

        StopFollowingSelectedEntity();

        var result = await FollowEntityController.IdentifyLayerAsync(_dynamicEntityLayer, eventArgs.Position, 2d);
        if (result?.GeoElements?.FirstOrDefault() is DynamicEntityObservation observation)
        {
            SelectedEntity = observation.GetDynamicEntity();
            if (SelectedEntity is not null)
            {
                _dynamicEntityLayer.SelectDynamicEntity(SelectedEntity);
                FollowSelectedEntity();
            }
        }
    }

    [RelayCommand]
    private void FollowModeChanged(FollowMode followMode)
    {
        StopFollowingSelectedEntity();

        FollowMode = followMode;
        if ((!IsSceneView && FollowMode == FollowMode.CameraController)
            || (IsSceneView && FollowMode == FollowMode.LocationDataSource))
        {
            ToggleGeoView();
        }
    }

    private void ToggleGeoView()
    {
        try
        {
            if (_warehouseLayer is null || _dynamicEntityLayer is null || _renderer is null)
                return;

            GeoModel.OperationalLayers.Clear();
            if (IsSceneView)
            {
                GeoModel = new Map(BasemapStyle.ArcGISStreets) { InitialViewpoint = new Viewpoint(_extent) };
                _dynamicEntityLayer.Renderer = _renderer;
            }
            else
            {
                GeoModel = new Scene(BasemapStyle.ArcGISStreetsRelief)
                {
                    InitialViewpoint = new Viewpoint(_extent),
                    BaseSurface = new Surface { ElevationSources = { new ArcGISTiledElevationSource(new Uri(ElevationUrl)) } }
                };
                var sceneRenderer = (UniqueValueRenderer)_renderer.Clone();
                sceneRenderer.DefaultSymbol = CreateSceneSymbol(Color.Black);
                sceneRenderer.UniqueValues[0].Symbol = CreateSceneSymbol(Color.Blue);
                sceneRenderer.UniqueValues[1].Symbol = CreateSceneSymbol(Color.Red);
                sceneRenderer.SceneProperties.HeadingExpression = "[Heading]";
                _dynamicEntityLayer.Renderer = sceneRenderer;
            }
            GeoModel.OperationalLayers.Add(_warehouseLayer);
            GeoModel.OperationalLayers.Add(_dynamicEntityLayer);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "MapView / SceneView toggle error");
        }
    }

    private static SimpleMarkerSceneSymbol CreateSceneSymbol(Color color)
    {
        return new SimpleMarkerSceneSymbol(SimpleMarkerSceneSymbolStyle.Cone, color, 45, 15, 15, SceneSymbolAnchorPosition.Bottom)
        {
            Pitch = -90d
        };
    }

    #region FollowSelectedEntity

    private void FollowSelectedEntity()
    {
        if (FollowMode == FollowMode.Manual)
            FollowSelectedEntity_Manual();
        else if (FollowMode == FollowMode.LocationDataSource)
            FollowSelectedEntity_LocationDataSource();
        else if (FollowMode == FollowMode.CameraController)
            FollowSelectedEntity_CameraController();
    }

    private void StopFollowingSelectedEntity()
    {
        if (FollowMode == FollowMode.Manual)
            StopFollowingSelectedEntity_Manual();
        else if (FollowMode == FollowMode.LocationDataSource)
            StopFollowingSelectedEntity_LocationDataSource();
        else if (FollowMode == FollowMode.CameraController)
            StopFollowingSelectedEntity_CameraController();

        SelectedEntity = null;
        _dynamicEntityLayer?.ClearSelection();
    }

    #endregion

    #region Follow - Manual

    private void FollowSelectedEntity_Manual()
    {
        if (SelectedEntity is not null)
            SelectedEntity.DynamicEntityChanged += SelectedEntity_DynamicEntityChanged;
    }

    private void StopFollowingSelectedEntity_Manual()
    {
        if (SelectedEntity is not null)
            SelectedEntity.DynamicEntityChanged -= SelectedEntity_DynamicEntityChanged;
    }

    private async void SelectedEntity_DynamicEntityChanged(object? sender, DynamicEntityChangedEventArgs e)
    {
        if (e.ReceivedObservation?.Geometry is MapPoint point)
            await FollowEntityController.PanToAsync(point);
    }

    #endregion

    #region Follow with LocationDataSource

    private async void FollowSelectedEntity_LocationDataSource()
    {
        if (SelectedEntity is not null)
        {
            var followSource = new EntityLocationDataSource(SelectedEntity);
            await FollowEntityController.UpdateLocationDisplayAsync(followSource);
        }
    }

    private async void StopFollowingSelectedEntity_LocationDataSource()
    {
        await FollowEntityController.UpdateLocationDisplayAsync(null);
    }

    #endregion

    #region Follow with CameraController

    private void FollowSelectedEntity_CameraController()
    {
        if (SelectedEntity is not null)
        {
            var cameraController = new OrbitGeoElementCameraController(SelectedEntity, 1000d);
            FollowEntityController.UpdateCameraController(cameraController);
        }
    }

    private void StopFollowingSelectedEntity_CameraController()
    {
        FollowEntityController.UpdateCameraController(null);
    }

    #endregion
}
