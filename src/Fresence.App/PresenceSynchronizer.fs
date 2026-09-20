namespace Fresence.App

open System.Threading.Tasks
open Fresence.Discord
open Fresence.Media

type PresenceSyncResult =
    | Skipped
    | Updated
    | Cleared
    | Failed of string

type PresenceSynchronizer(
    mediaSessionReader: IMediaSessionReader,
    discordPresenceClient: IDiscordPresenceClient,
    artworkProvider: IAlbumArtworkProvider
) =
    let mutable isConnected = false
    let mutable hasSynchronized = false
    let mutable previousSelection: SelectedMediaSession option = None

    /// <summary>
    /// 取得最近一次成功同步的媒體工作階段。
    /// </summary>
    member _.CurrentSelection = previousSelection

    /// <summary>
    /// 讀取媒體工作階段，並依變更結果更新或清除 Discord Presence。
    /// </summary>
    member _.SynchronizeAsync() =
        task {
            let! connectionResult =
                if isConnected then
                    Task.FromResult (Ok ())
                else
                    discordPresenceClient.ConnectAsync ()

            match connectionResult with
            | Error message -> return Failed message
            | Ok () ->
                isConnected <- true

                let! sessions = mediaSessionReader.GetSessionsAsync ()
                let selection = BrowserMediaSessionSelector.select sessions

                if hasSynchronized
                   && not (MediaSessionChangeDetector.hasChanged previousSelection selection) then
                    return Skipped
                else
                    match selection with
                    | Some selected ->
                        let! artworkUrl = artworkProvider.GetArtworkUrlAsync selected.Session.Track
                        let activity = DiscordActivityMapper.withArtwork artworkUrl selected.Session
                        let! updateResult = discordPresenceClient.SetActivityAsync activity

                        match updateResult with
                        | Ok () ->
                            previousSelection <- selection
                            hasSynchronized <- true
                            return Updated
                        | Error message -> return Failed message
                    | None ->
                        let! clearResult = discordPresenceClient.ClearActivityAsync ()

                        match clearResult with
                        | Ok () ->
                            previousSelection <- None
                            hasSynchronized <- true
                            return Cleared
                        | Error message -> return Failed message
        }

