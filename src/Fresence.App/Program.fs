namespace Fresence.App

open System
open Avalonia

module Program =

    /// <summary>
    /// 建立並設定 Avalonia 桌面應用程式。
    /// </summary>
    [<CompiledName "BuildAvaloniaApp">]
    let buildAvaloniaApp () : AppBuilder =
        AppBuilder
            .Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace(areas = Array.empty)

    /// <summary>
    /// 啟動 Avalonia 傳統桌面應用程式生命週期。
    /// </summary>
    [<EntryPoint; STAThread>]
    let main argv =
        (buildAvaloniaApp ()).StartWithClassicDesktopLifetime argv
