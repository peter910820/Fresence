# Fresence

Fresence 是一款簡易的Windows專用桌面應用程式，會讀取瀏覽器透過 Windows Global System Media Transport Controls（GSMTC）提供的媒體資訊，並同步至 Discord Rich Presence。

## 系統需求

- Windows 10 1809（10.0.17763）或更新版本
- .NET 10 SDK
- 已安裝並登入的 Discord Desktop

## 使用方式

1. 前往 [Discord Developer Portal](https://discord.com/developers/applications) 建立應用程式。
2. 在應用程式的 **General Information** 複製 **Application ID**。
3. 啟動 Discord Desktop。
4. 執行 Fresence：

```powershell
dotnet run --project src/Fresence.App/Fresence.App.fsproj
```

5. 在視窗中輸入 Application ID，然後按下「開始同步」。

若編譯時已指定 `-p:DiscordApplicationId=...`，視窗不會顯示輸入欄，按下「開始同步」即可。

> Application ID 不是 Discord Bot Token；請勿將 Bot Token 輸入 Fresence 或提交至版本控制。

## 發布

產出不需安裝 .NET 的 Windows x64 單檔，並寫入 Application ID：

```powershell
dotnet publish src/Fresence.App/Fresence.App.fsproj `
  --configuration Release `
  --runtime win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -p:DiscordApplicationId=你的ApplicationID `
  --output .\publish\win-x64
```

## 限制

- 僅能同步實際提供 GSMTC 資訊的播放器或網站。
- 不會取得或上傳專輯封面。
- 拖曳播放器進度後，Discord 時間可能要等下一次歌曲或播放狀態事件才會校正。

## 開發

建置與測試：

```powershell
dotnet build Fresence.slnx --configuration Release
dotnet test Fresence.slnx --no-build --configuration Release
```