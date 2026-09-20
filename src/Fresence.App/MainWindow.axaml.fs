namespace Fresence.App

open System
open System.Net.Http
open System.Threading
open System.Threading.Tasks
open Avalonia
open Avalonia.Controls
open Avalonia.Markup.Xaml
open Avalonia.Threading
open Fresence.Discord
open Fresence.Media

type MainWindow() as this =
    inherit Window()

    let mutable cancellationTokenSource: CancellationTokenSource option = None
    let mutable mediaChangeSubscription: IDisposable option = None
    let mutable discordClient: IDisposable option = None
    let mutable albumArtworkProvider: IDisposable option = None
    let mutable albumArtworkHttpClient: HttpClient option = None

    do
        this.InitializeComponent()
        this.ApplyCompiledApplicationId()
        (this.FindControl<Button> "StartButton").Click.Add(fun _ -> this.StartSynchronization())

        (this.FindControl<Button> "StopButton")
            .Click.Add(fun _ ->
                this.StopSynchronization()
                (this.FindControl<TextBlock> "StatusTextBlock").Text <- "已停止同步")

    /// <summary>
    /// 載入主視窗的 Avalonia XAML。
    /// </summary>
    member private this.InitializeComponent() = AvaloniaXamlLoader.Load(this)

    /// <summary>
    /// 若編譯時已指定 Application ID，則隱藏輸入欄位。
    /// </summary>
    member private this.ApplyCompiledApplicationId() =
        if not (String.IsNullOrWhiteSpace CompiledApplicationId.Value) then
            (this.FindControl<Border> "ApplicationIdCard").IsVisible <- false
            this.Height <- 300.0

    /// <summary>
    /// 將同步結果與目前選取的歌曲顯示在視窗中。
    /// </summary>
    member private this.UpdateStatus(synchronizer: PresenceSynchronizer, result: PresenceSyncResult) =
        let statusTextBlock = this.FindControl<TextBlock> "StatusTextBlock"
        let sourceTextBlock = this.FindControl<TextBlock> "SourceTextBlock"
        let trackTextBlock = this.FindControl<TextBlock> "TrackTextBlock"

        statusTextBlock.Text <-
            match result with
            | Skipped -> "同步中：歌曲未變更"
            | Updated -> "同步中：已更新 Discord Presence"
            | Cleared -> "同步中：已清除 Discord Presence"
            | Failed message -> $"同步失敗：{message}"

        match synchronizer.CurrentSelection with
        | Some selected ->
            sourceTextBlock.Text <- $"來源：{selected.Browser}"
            trackTextBlock.Text <- $"歌曲：{selected.Session.Track.Title} — {selected.Session.Track.Artist}"
        | None ->
            sourceTextBlock.Text <- "來源：—"
            trackTextBlock.Text <- "歌曲：—"

    /// <summary>
    /// 取消媒體事件訂閱並釋放 Discord 與專輯封面資源。
    /// </summary>
    member private this.StopSynchronization() =
        cancellationTokenSource
        |> Option.iter (fun source ->
            source.Cancel()
            source.Dispose())

        mediaChangeSubscription
        |> Option.iter (fun subscription -> subscription.Dispose())

        discordClient |> Option.iter (fun client -> client.Dispose())

        albumArtworkProvider |> Option.iter (fun provider -> provider.Dispose())

        albumArtworkHttpClient |> Option.iter (fun client -> client.Dispose())

        cancellationTokenSource <- None
        mediaChangeSubscription <- None
        discordClient <- None
        albumArtworkProvider <- None
        albumArtworkHttpClient <- None
        (this.FindControl<Button> "StartButton").IsEnabled <- true
        (this.FindControl<Button> "StopButton").IsEnabled <- false

    /// <summary>
    /// 使用輸入的 Discord Application ID 建立事件驅動的同步作業。
    /// </summary>
    member private this.StartSynchronization() =
        let applicationId =
            if String.IsNullOrWhiteSpace CompiledApplicationId.Value then
                (this.FindControl<TextBox> "ApplicationIdTextBox").Text
            else
                CompiledApplicationId.Value

        if String.IsNullOrWhiteSpace applicationId then
            (this.FindControl<TextBlock> "StatusTextBlock").Text <- "請先輸入 Discord Application ID"
        else
            this.StopSynchronization()

            let cancellationSource = new CancellationTokenSource()
            let cancellationToken = cancellationSource.Token
            let client = new DiscordIpcClient(applicationId)

            let artworkHttpClient =
                new HttpClient(Timeout = TimeSpan.FromSeconds 10.0, MaxResponseContentBufferSize = 1_048_576L)

            let artworkProvider =
                new ItunesAlbumArtworkProvider(artworkHttpClient, TimeSpan.FromSeconds 10.0)

            let mediaSessionReader = WindowsMediaSessionReader()

            let synchronizer =
                PresenceSynchronizer(
                    mediaSessionReader :> IMediaSessionReader,
                    client :> IDiscordPresenceClient,
                    artworkProvider :> IAlbumArtworkProvider
                )

            cancellationTokenSource <- Some cancellationSource
            discordClient <- Some(client :> IDisposable)
            albumArtworkProvider <- Some(artworkProvider :> IDisposable)
            albumArtworkHttpClient <- Some artworkHttpClient
            (this.FindControl<Button> "StartButton").IsEnabled <- false
            (this.FindControl<Button> "StopButton").IsEnabled <- true
            (this.FindControl<TextBlock> "StatusTextBlock").Text <- "正在訂閱媒體事件"

            Task.Run(
                Func<Task>(fun () ->
                    task {
                        // 每次 GSMTC 事件觸發時，在背景執行同步，避免阻塞 UI 執行緒。
                        let synchronize () =
                            Task.Run(
                                Func<Task>(fun () ->
                                    task {
                                        if not cancellationToken.IsCancellationRequested then
                                            let! result = synchronizer.SynchronizeAsync()

                                            if not cancellationToken.IsCancellationRequested then
                                                Dispatcher.UIThread.Post(fun () ->
                                                    this.UpdateStatus(synchronizer, result))
                                    })
                            )
                            |> ignore

                        let! subscription =
                            (mediaSessionReader :> IMediaSessionChangeNotifier).SubscribeToChangesAsync(synchronize)

                        mediaChangeSubscription <- Some subscription

                        if cancellationToken.IsCancellationRequested then
                            subscription.Dispose()
                            mediaChangeSubscription <- None

                    })
            )
            |> ignore

    /// <summary>
    /// 在關閉視窗前停止同步並釋放相關資源。
    /// </summary>
    override this.OnClosed(eventArgs) =
        this.StopSynchronization()
        base.OnClosed eventArgs
