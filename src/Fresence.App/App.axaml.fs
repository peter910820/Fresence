namespace Fresence.App

open Avalonia
open Avalonia.Controls.ApplicationLifetimes
open Avalonia.Markup.Xaml

type App() =
    inherit Application()

    /// <summary>
    /// 載入應用程式的 Avalonia XAML 資源。
    /// </summary>
    override this.Initialize() =
            AvaloniaXamlLoader.Load this

    /// <summary>
    /// 在 Avalonia 框架完成初始化後建立主視窗。
    /// </summary>
    override this.OnFrameworkInitializationCompleted() =
        match this.ApplicationLifetime with
        | :? IClassicDesktopStyleApplicationLifetime as desktop ->
             desktop.MainWindow <- MainWindow ()
        | _ -> ()

        base.OnFrameworkInitializationCompleted ()
