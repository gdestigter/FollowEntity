using System.Text;
using Esri.ArcGISRuntime.Geometry;

namespace FollowEntity;

public class ActiveRoute
{
    public ActiveRoute(Polyline routePath, double routeSpeed)
    {
        RouteId = RandomRouteId();
        RoutePath = routePath;
        AverageMetersPerSecond = routeSpeed;
        LastPoint = (MapPoint)StartPoint.Project(SpatialReferences.Wgs84);
        RouteStatus = RouteStatus.EnRoute;
    }

    #region Properties

    public string RouteId { get; }

    public Polyline RoutePath { get; private set; }

    public MapPoint StartPoint => RoutePath.Parts[0].StartPoint!;

    public MapPoint EndPoint => RoutePath.Parts[0].EndPoint!;

    public double AverageMetersPerSecond { get; private set; }

    public MapPoint LastPoint { get; private set; }

    public double SecondsTraveled { get; set; }

    public double CurrentHeading { get; private set; }

    public RouteStatus RouteStatus { get; private set; }

    #endregion

    public MapPoint CalculateNextPoint(double seconds = 1d)
    {
        SecondsTraveled += seconds;
        var distance = SecondsTraveled * AverageMetersPerSecond;
        var point = (MapPoint)RoutePath.CreatePointAlong(distance).Project(SpatialReferences.Wgs84);
        if (distance >= RoutePath.Length())
            RouteStatus = RouteStatus is RouteStatus.EnRoute ? RouteStatus.AtDestination : RouteStatus.Complete;
        CurrentHeading = GeometryEngine.DistanceGeodetic(point, LastPoint, null, null, GeodeticCurveType.Geodesic).Azimuth1 - 180.0;
        LastPoint = point;
        return LastPoint;
    }

    public void ResetRoutePath(Polyline routePath, double routeSpeed)
    {
        RoutePath = routePath;
        SecondsTraveled = 0d;
        AverageMetersPerSecond = routeSpeed;
        RouteStatus = RouteStatus.Returning;
    }

    private static string RandomRouteId()
    {
        return RandomCode("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-", 6);
    }

    private static string RandomCode(string characters, int length)
    {
        Random random = new();
        StringBuilder code = new();
        for (int i = 0; i < length; i++)
        {
            int index = random.Next(characters.Length);
            code.Append(characters[index]);
        }
        return code.ToString();
    }
}

public enum RouteStatus
{
    EnRoute = 0,
    Returning,
    AtDestination,
    Complete
}
