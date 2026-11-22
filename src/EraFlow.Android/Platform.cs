using Android.App;
using Android.Views;
using Android.Webkit;
using Java.Interop;
using SkiaSharp;
using System.Text.Json;

namespace EraFlow;

public sealed class JsBridge(Action<string> callback) : Java.Lang.Object
{
 [JavascriptInterface,Export("call")] public void Call(string json)=>callback(json);
}
public sealed class LocalClient(Action loaded,Func<string,bool> allowed) : WebViewClient
{
 public override void OnPageFinished(WebView? view,string? url) { base.OnPageFinished(view,url); loaded(); }
 public override bool ShouldOverrideUrlLoading(WebView? view,IWebResourceRequest? request)=>!allowed(request?.Url?.ToString()??"");
 public override bool ShouldOverrideUrlLoading(WebView? view,string? url)=>!allowed(url??"");
}
public static class Platform
{
 public static void Configure(Activity activity,WebView view,bool play)
 {
#if ERAFLOW_TEST
  WebView.SetWebContentsDebuggingEnabled(true);
#endif
  view.Settings.JavaScriptEnabled=true; view.Settings.DomStorageEnabled=true; view.Settings.AllowFileAccess=false; view.Settings.AllowContentAccess=false;
  view.Settings.SetSupportZoom(false); view.Settings.MediaPlaybackRequiresUserGesture=true;
  view.SetBackgroundColor(Android.Graphics.Color.ParseColor(play?"#11150f":"#f5f7f0"));
 }
 /// <param name="fullBleed">
 /// ゲーム画面用。ステータスバー・ナビゲーションバー・ノッチの余白を一切足さず、
 /// 画面の端まで WebView を広げる。余白を足すと、横画面でノッチのぶんだけ
 /// 左に黒帯が出て「ゲーム画面が完全に覆わない」状態になる。
 /// </param>
 public static void SetContent(Activity activity,WebView view,bool fullBleed=false)
 {
  var root=new Android.Widget.FrameLayout(activity);root.AddView(view,new Android.Widget.FrameLayout.LayoutParams(-1,-1));activity.SetContentView(root);
  if(OperatingSystem.IsAndroidVersionAtLeast(30))
  {
   if(fullBleed)
   {
    // 余白は付けない。IME が出たときだけ縮めて、入力欄が隠れないようにする
    root.SetOnApplyWindowInsetsListener(new ImeInsets());root.RequestApplyInsets();
   }
   else
   {
    root.SetOnApplyWindowInsetsListener(new Insets());root.RequestApplyInsets();
   }
  }
 }
 sealed class Insets:Java.Lang.Object,Android.Views.View.IOnApplyWindowInsetsListener
 {
  public WindowInsets OnApplyWindowInsets(Android.Views.View? view,WindowInsets? insets)
  {
   if(insets!=null && OperatingSystem.IsAndroidVersionAtLeast(30)){var b=insets.GetInsets(WindowInsets.Type.SystemBars()|WindowInsets.Type.DisplayCutout()|WindowInsets.Type.Ime());view?.SetPadding(b.Left,b.Top,b.Right,b.Bottom);}return insets!;
  }
 }
 /// <summary>全画面表示用。IME の分だけ下端を空ける。</summary>
 sealed class ImeInsets:Java.Lang.Object,Android.Views.View.IOnApplyWindowInsetsListener
 {
  public WindowInsets OnApplyWindowInsets(Android.Views.View? view,WindowInsets? insets)
  {
   if(insets!=null && OperatingSystem.IsAndroidVersionAtLeast(30))
   {
    var b=insets.GetInsets(WindowInsets.Type.Ime());
    view?.SetPadding(0,0,0,b.Bottom);
   }
   return insets!;
  }
 }
 public static void Send(Activity a,WebView web,string method,object data)=>a.RunOnUiThread(()=>web.EvaluateJavascript($"window.Era?.{method}({JsonSerializer.Serialize(data)})",null));
 public static void Fonts(Activity activity)
 {
  using var input=activity.Assets!.Open("fonts/BIZUDGothic-Regular.ttf"); using var memory=new MemoryStream(); input.CopyTo(memory);
  using var data=SKData.CreateCopy(memory.ToArray());var face=SKTypeface.FromData(data);System.Drawing.FontResolver.Register(face.FamilyName,face);System.Drawing.FontResolver.Fallback=face;
 }
}
