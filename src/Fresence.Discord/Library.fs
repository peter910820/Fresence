module Fresence.Discord

open System
open System.Threading.Tasks
open DiscordRPC
open Fresence.Media

/// <summary>
/// Discord Rich Presence 的顯示資料。
/// </summary>
type DiscordActivity = { Details: string; State: string }

/// <summary>
/// 管理 Discord 本機 IPC 連線與 Rich Presence 更新的契約。
/// </summary>
type IDiscordPresenceClient =
    abstract ConnectAsync: unit -> Task<Result<unit, string>>
    abstract SetActivityAsync: DiscordActivity -> Task<Result<unit, string>>
    abstract ClearActivityAsync: unit -> Task<Result<unit, string>>

/// <summary>
/// 將 Fresence 的媒體資料轉換為 Discord 顯示資料。
/// </summary>
module DiscordActivityMapper =
    /// <summary>
    /// 映射資料。
    /// </summary>
    let fromMediaSession (session: MediaSession) =
        { Details = session.Track.Title
          State = $"{session.Track.Artist} — {session.Track.AlbumTitle}" }

/// <summary>
/// 透過 DiscordRichPresence 用戶端實作 Rich Presence IPC。
/// </summary>
type DiscordIpcClient(applicationId: string) =
    let client = new DiscordRpcClient(applicationId)

    let execute action =
        task {
            try
                action ()
                return Ok ()
            with ex ->
                return Error ex.Message
        }

    interface IDiscordPresenceClient with
        member _.ConnectAsync() =
            execute (fun () -> client.Initialize() |> ignore)

        member _.SetActivityAsync activity =
            execute (fun () ->
                client.SetPresence(
                    RichPresence(
                        Details = activity.Details,
                        State = activity.State
                    )
                ))

        member _.ClearActivityAsync() =
            execute client.ClearPresence

    interface IDisposable with
        member _.Dispose() = client.Dispose()
