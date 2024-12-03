using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Location;
using Esri.ArcGISRuntime.Mapping;
using Esri.ArcGISRuntime.Toolkit.UI;
using Esri.ArcGISRuntime.UI;
using Esri.ArcGISRuntime.UI.Controls;

namespace FollowEntity;

public class FollowEntityController : GeoViewController
{
    public async Task UpdateLocationDisplayAsync(LocationDataSource? dataSource)
    {
        if (ConnectedView is not MapView mapView)
            return;

        if (mapView.LocationDisplay.DataSource is not null)
            await mapView.LocationDisplay.DataSource.StopAsync();

        if (dataSource is null)
        {
            mapView.LocationDisplay.DataSource = null;
            mapView.LocationDisplay.IsEnabled = false;
            return;
        }

        mapView.LocationDisplay.DataSource = dataSource;
        mapView.LocationDisplay.AutoPanMode = LocationDisplayAutoPanMode.Recenter;
        mapView.LocationDisplay.InitialZoomScale = mapView.MapScale;
        mapView.LocationDisplay.ShowLocation = false;
        mapView.LocationDisplay.WanderExtentFactor = 0.5d;
        mapView.LocationDisplay.IsEnabled = true;
    }

    public void UpdateCameraController(CameraController? cameraController)
    {
        if (ConnectedView is not SceneView sceneView)
            return;
        sceneView.CameraController = cameraController;
    }

    public Task PanToAsync(MapPoint? center)
    {
        if (ConnectedView is null || center is null)
            return Task.CompletedTask;
        return ConnectedView.SetViewpointAsync(new Viewpoint(center));
    }
}
