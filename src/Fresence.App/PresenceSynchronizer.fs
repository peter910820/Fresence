namespace Fresence.App

open System
open System.Threading
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
    discordPresenceClient: IDiscordPresenceClient
) =
    let mutable isConnected = false
    let mutable hasSynchronized = false
    let mutable previousSelection: SelectedMediaSession option = None

    member _.SynchronizeAsync() =
        task {
            let! connectionResult =
                if isConnected then
                    Task.FromResult(Ok ())
                else
                    discordPresenceClient.ConnectAsync()

            match connectionResult with
            | Error message -> return Failed message
            | Ok () ->
                isConnected <- true

                let! sessions = mediaSessionReader.GetSessionsAsync()
                let selection = BrowserMediaSessionSelector.select sessions

                if hasSynchronized
                   && not (MediaSessionChangeDetector.hasChanged previousSelection selection) then
                    return Skipped
                else
                    match selection with
                    | Some selected ->
                        let activity = DiscordActivityMapper.fromMediaSession selected.Session
                        let! updateResult = discordPresenceClient.SetActivityAsync(activity)

                        match updateResult with
                        | Ok () ->
                            previousSelection <- selection
                            hasSynchronized <- true
                            return Updated
                        | Error message -> return Failed message
                    | None ->
                        let! clearResult = discordPresenceClient.ClearActivityAsync()

                        match clearResult with
                        | Ok () ->
                            previousSelection <- None
                            hasSynchronized <- true
                            return Cleared
                        | Error message -> return Failed message
        }

    member this.RunAsync(interval: TimeSpan, cancellationToken: CancellationToken) =
        task {
            try
                while not cancellationToken.IsCancellationRequested do
                    let! _ = this.SynchronizeAsync()
                    do! Task.Delay(interval, cancellationToken)
            with :? OperationCanceledException when cancellationToken.IsCancellationRequested ->
                ()
        }
