using System.Diagnostics;
using System.Windows;
using CommunityToolkit.Mvvm.DependencyInjection;
using Esri.ArcGISRuntime;
using FollowEntity.ViewModel;
using Microsoft.Extensions.DependencyInjection;

namespace FollowEntity;

public partial class App : Application
{
    public static MainViewModel MainViewModel => Ioc.Default.GetRequiredService<MainViewModel>();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            // TODO: Set your API key here
            // (Sign up at https://developers.arcgis.com/sign-up/ to get an API Key)
            ArcGISRuntimeEnvironment.ApiKey = "YOUR_API_KEY";

            ArcGISRuntimeEnvironment.Initialize();

            Ioc.Default.ConfigureServices(new ServiceCollection()
                .AddSingleton<MainViewModel>()
                .BuildServiceProvider());
        }
        catch (InvalidOperationException ex)
        {
            Debug.WriteLine(ex);
        }
    }
}
