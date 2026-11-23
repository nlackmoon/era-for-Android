using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Android.Webkit;
using MinorShift.Emuera.WebHost;
using System.Text.Json;

namespace EraFlow;

[Activity(Label="era for Android",Exported=false,Process=":game",Theme="@style/EraPlayTheme",ScreenOrientation=ScreenOrientation.SensorLandscape,WindowSoftInputMode=SoftInput.AdjustResize,ConfigurationChanges=ConfigChanges.ScreenSize|ConfigChanges.Orientation|ConfigChanges.KeyboardHidden)]
public sealed class GameActivity:Activity
{
 WebView web=null!;EmueraWebHost? host;bool exiting;string title="",gameId="",gamePath="";
 protected override async void OnCreate(Bundle? state)
 {
  base.OnCreate(state);Window!.AddFlags(WindowManagerFlags.KeepScreenOn);
  if(OperatingSystem.IsAndroidVersionAtLeast(28))WebView.SetDataDirectorySuffix("game");
  web=new WebView(this);Platform.Configure(this,web,true);Platform.SetContent(this,web,true);ApplyImmersive();web.AddJavascriptInterface(new JsBridge(Receive),"Android");
  title=Intent?.GetStringExtra("title")??"Era";gameId=Intent?.GetStringExtra("id")??"";
  web.SetWebViewClient(new LocalClient(()=>{Platform.Send(this,web,"title",title);Platform.Send(this,web,"settings",LoadSettings());},u=>u=="about:blank"||u.StartsWith("file:///android_asset/")||(host!=null&&u.StartsWith(host.Url))));
  web.LoadUrl("file:///android_asset/loading.html");
  try
  {
   string path=Path.GetFullPath(Intent?.GetStringExtra("path")??"");string root=Path.Combine(FilesDir!.AbsolutePath,"library")+Path.DirectorySeparatorChar;
   gamePath=path;
   bool external=Intent?.GetBooleanExtra("external",false)==true;
   if(external)
   {
    // 直读模式：目录在用户自己的存储里，只校验它确实是一个 Era 游戏目录。
    if(!Directory.Exists(Path.Combine(path,"erb"))||!Directory.Exists(Path.Combine(path,"csv")))throw new IOException("游戏目录无效。");
   }
   else if(!path.StartsWith(root,StringComparison.Ordinal))throw new IOException("游戏路径无效。");
   Platform.Fonts(this);MinorShift.Emuera.Runtime.Utils.PluginSystem.BuiltinPlugins.BrowserLauncher=_=>{};
   // 画面側の A− / A+ で決めたフォントサイズ (未設定ならゲームの emuera.config に従う)
   MinorShift.Emuera.Api.EmueraEngine.FontSizeOverride=ReadFontSize();
   // 文字を滲ませないため、端末の画素密度ぶんだけ高解像度で描く (論理レイアウトは据え置き)。
   // 3 倍は PNG 化が重く操作がもたつくので 2 倍で頭打ちにする。
   int scale=Math.Clamp((int)Math.Round(Resources?.DisplayMetrics?.Density??2f),1,2);
   host=new EmueraWebHost(path,1000,420,0,true,scale){Log=s=>Android.Util.Log.Info("EraFlowEngine",s)};await host.StartAsync();if(!exiting)web.LoadUrl(host.Url);
  }catch(Exception e){Android.Util.Log.Error("EraFlowEngine",e.ToString());Platform.Send(this,web,"error","游戏未能启动："+e.Message);}
 }
 /// <summary>状态栏/导航栏都收起来，游戏画面铺满整屏 (UEmuera 那种全屏)。</summary>
 void ApplyImmersive()
 {
  Window!.AddFlags(WindowManagerFlags.Fullscreen);
  Window.DecorView.SystemUiVisibility=(StatusBarVisibility)(SystemUiFlags.LayoutStable|SystemUiFlags.LayoutFullscreen|SystemUiFlags.LayoutHideNavigation|SystemUiFlags.HideNavigation|SystemUiFlags.Fullscreen|SystemUiFlags.ImmersiveSticky);
  // 横画面ではノッチが左右に出る。既定 (自動) だとノッチを避けて黒帯が残るので、
  // 画面の端まで描かせる。ゲームは黒背景なので切れても実害が無い
  if(OperatingSystem.IsAndroidVersionAtLeast(28))
  {
   var attrs=Window.Attributes!;
   attrs.LayoutInDisplayCutoutMode=OperatingSystem.IsAndroidVersionAtLeast(30)
    ? LayoutInDisplayCutoutMode.Always
    : LayoutInDisplayCutoutMode.ShortEdges;
   Window.Attributes=attrs;
  }
 }
 public override void OnWindowFocusChanged(bool hasFocus){base.OnWindowFocusChanged(hasFocus);if(hasFocus)ApplyImmersive();}
 object LoadSettings()
 {
  var p=GetSharedPreferences("player",Android.Content.FileCreationMode.Private)!;string json=p.GetString(gameId,"{\"scale\":1,\"images\":true,\"haptic\":true,\"left\":false,\"compact\":false}")!;try{return JsonSerializer.Deserialize<JsonElement>(json);}catch{return new{scale=1,images=true,haptic=true,left=false,compact=false};}
 }
 /// <summary>プレイヤー設定の fontSize (0 ならゲーム任せ)。</summary>
 int ReadFontSize()
 {
  try{if(LoadSettings() is JsonElement s&&s.ValueKind==JsonValueKind.Object&&s.TryGetProperty("fontSize",out var v)&&v.TryGetInt32(out int n))return n;}catch{}
  return 0;
 }
 void Receive(string json)=>RunOnUiThread(()=>{
  try{using var doc=JsonDocument.Parse(json);var d=doc.RootElement;switch(d.GetProperty("action").GetString())
  {
   case "home":ConfirmExit();break;
   case "haptic":web.PerformHapticFeedback(FeedbackConstants.KeyboardTap);break;
   case "settings":GetSharedPreferences("player",Android.Content.FileCreationMode.Private)!.Edit()!.PutString(gameId,d.GetProperty("value").GetRawText())!.Apply();break;
   // 转向锁: 固定すると今の向きで固定、解除すると端末の向きに従う
   case "orient":RequestedOrientation=d.GetProperty("value").GetBoolean()?ScreenOrientation.Locked:ScreenOrientation.SensorLandscape;break;
   // 左メニューの「保存日志」。ゲームの emuera.log をアプリ専用の外部領域へ複製する
   case "savelog":SaveLog();break;
  }}catch(Exception e){Android.Util.Log.Warn("EraFlow",e.Message);}
 });
 /// <summary>emuera.log を /sdcard/Android/data/&lt;pkg&gt;/files/ へ複製する。</summary>
 void SaveLog()
 {
  try
  {
   string src=Path.Combine(gamePath,"emuera.log");
   if(!File.Exists(src)){Platform.Send(this,web,"tip","游戏还没有写日志（发生错误后才会生成）");return;}
   string dir=GetExternalFilesDir(null)?.AbsolutePath??FilesDir!.AbsolutePath;
   string name="emuera-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".log";
   string dst=Path.Combine(dir,name);
   File.Copy(src,dst,true);
   Platform.Send(this,web,"tip","日志已保存到 Android/data/"+PackageName+"/files/"+name);
  }
  catch(Exception e){Platform.Send(this,web,"tip","保存日志失败："+e.Message);}
 }
 void ConfirmExit(){new AlertDialog.Builder(this).SetTitle("返回游戏库？").SetMessage("请先使用游戏内的存档功能保存进度。").SetNegativeButton("继续游玩",(_,_)=>{}).SetPositiveButton("返回游戏库",(_,_)=>{exiting=true;Finish();}).Show();}
 public override void OnBackPressed()=>ConfirmExit();
 protected override void OnPause(){base.OnPause();web?.OnPause();}
 protected override void OnResume(){base.OnResume();web?.OnResume();}
 protected override void OnDestroy(){exiting=true;web?.Destroy();base.OnDestroy();new Handler(Looper.MainLooper!).PostDelayed(()=>Android.OS.Process.KillProcess(Android.OS.Process.MyPid()),200);}
}
