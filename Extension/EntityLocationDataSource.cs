using Esri.ArcGISRuntime.Geometry;
using Esri.ArcGISRuntime.Location;
using Esri.ArcGISRuntime.RealTime;

namespace FollowEntity;

public class EntityLocationDataSource : LocationDataSource
{
    private readonly DynamicEntity _followEntity;

    public EntityLocationDataSource(DynamicEntity dynamicEntity)
    {
        _followEntity = dynamicEntity;
        UpdateLocationFromObservation(_followEntity.GetLatestObservation());
    }

    public string HeadingField { get; set; } = "Heading";

    protected override Task OnStartAsync()
    {
        _followEntity.DynamicEntityChanged += FollowEntity_DynamicEntityChanged;
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _followEntity.DynamicEntityChanged -= FollowEntity_DynamicEntityChanged;
        return Task.CompletedTask;
    }

    private void FollowEntity_DynamicEntityChanged(object? sender, DynamicEntityChangedEventArgs e)
    {
        UpdateLocationFromObservation(e.ReceivedObservation);
    }

    private void UpdateLocationFromObservation(DynamicEntityObservation? observation)
    {
        if (observation?.Geometry is not MapPoint point)
            return;

        double course = 0d;
        if (observation.Attributes.TryGetValue(HeadingField, out object? headingValue))
            course = Convert.ToDouble(headingValue);

        UpdateLocation(new Location(point, 0d, 1d, course, false));
    }
}
