using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

// Official website rendered untouched. No password fields, injected scripts,
// request-body listeners, DOM credential extraction, or other-browser profiles.
internal sealed class HoyoLoginWindow:Form {
    readonly WebView2 browser=new WebView2();
    readonly Label status=new Label{AutoSize=true,Padding=new Padding(12),Text="HoYoLAB 공식 로그인 화면을 준비하고 있어요."};
    readonly Button check=new Button{Text="로그인 확인",AutoSize=true,Padding=new Padding(8)};
    readonly System.Windows.Forms.Timer poll=new System.Windows.Forms.Timer{Interval=2500};
    readonly CancellationTokenSource cancellation=new CancellationTokenSource();
    readonly IGenshinDataProvider provider=new HoYoLabProvider();
    readonly Func<Dictionary<string,string>,List<GenshinRole>,Task> accept;
    readonly string root;
    bool closing,checking,finished;string previous="";DateTime requestAfter=DateTime.MinValue;
    internal HoyoLoginWindow(string dataRoot,Func<Dictionary<string,string>,List<GenshinRole>,Task> accepted){
        root=dataRoot;accept=accepted;Text="HoYoLAB 공식 로그인 · KoruGaming";AutoScaleMode=AutoScaleMode.Dpi;ClientSize=new Size(1000,800);MinimumSize=new Size(600,500);StartPosition=FormStartPosition.CenterParent;
        Font=new Font("맑은 고딕",10);BackColor=Color.White;
        var top=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,ColumnCount=1};status.Dock=DockStyle.Fill;top.Controls.Add(status);
        top.Resize+=delegate{status.MaximumSize=new Size(Math.Max(250,top.Width-10),0);};
        var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,Padding=new Padding(10),FlowDirection=FlowDirection.RightToLeft};
        var cancel=new Button{Text="닫기",AutoSize=true,Padding=new Padding(8)};cancel.Click+=delegate{Close();};bottom.Controls.Add(cancel);bottom.Controls.Add(check);
        browser.Dock=DockStyle.Fill;Controls.Add(browser);Controls.Add(top);Controls.Add(bottom);
        check.Click+=async delegate{await CheckSession(true);};poll.Tick+=async delegate{await CheckSession(false);};
        Shown+=async delegate{await Initialize();};
        FormClosing+=delegate{closing=true;poll.Stop();cancellation.Cancel();if(browser.CoreWebView2!=null)try{browser.CoreWebView2.CookieManager.DeleteAllCookies();}catch{}};
        FormClosed+=delegate{poll.Dispose();browser.Dispose();cancellation.Dispose();previous="";};
    }
    internal static bool IsOfficial(string url){Uri uri;if(!Uri.TryCreate(url,UriKind.Absolute,out uri)||uri.Scheme!="https"||!uri.IsDefaultPort)return false;
        string host=uri.IdnHost.ToLowerInvariant();return host=="hoyolab.com"||host.EndsWith(".hoyolab.com",StringComparison.Ordinal)||host=="hoyoverse.com"||host.EndsWith(".hoyoverse.com",StringComparison.Ordinal)||host=="account.mihoyo.com";
    }
    async Task Initialize(){try{
        var area=Screen.FromControl(this).WorkingArea;Size=new Size(Math.Min(Width,area.Width-40),Math.Min(Height,area.Height-40));CenterToParent();
        var environment=await CoreWebView2Environment.CreateAsync(null,Path.Combine(root,"HoyoLoginBrowser"));
        if(closing)return;var options=environment.CreateCoreWebView2ControllerOptions();options.ProfileName="HoyoLogin";options.IsInPrivateModeEnabled=true;
        await browser.EnsureCoreWebView2Async(environment,options);if(closing)return;var c=browser.CoreWebView2;
        if(!c.Profile.IsInPrivateModeEnabled)throw new InvalidOperationException();
        c.Settings.AreDevToolsEnabled=false;c.Settings.AreDefaultContextMenusEnabled=false;c.Settings.IsStatusBarEnabled=true;c.Settings.IsWebMessageEnabled=false;
        c.Settings.AreHostObjectsAllowed=false;c.Settings.AreBrowserAcceleratorKeysEnabled=false;
        c.Profile.IsPasswordAutosaveEnabled=false;c.Profile.IsGeneralAutofillEnabled=false;
        c.PermissionRequested+=delegate(object s,CoreWebView2PermissionRequestedEventArgs e){e.State=CoreWebView2PermissionState.Deny;};
        c.DownloadStarting+=delegate(object s,CoreWebView2DownloadStartingEventArgs e){e.Cancel=true;};
        c.NavigationStarting+=delegate(object s,CoreWebView2NavigationStartingEventArgs e){if(!IsOfficial(e.Uri)){e.Cancel=true;status.Text="외부 사이트 이동을 차단했어요. HoYoLAB 이메일 로그인을 이용해 주세요.";}};
        c.NewWindowRequested+=delegate(object s,CoreWebView2NewWindowRequestedEventArgs e){e.Handled=true;if(IsOfficial(e.Uri))c.Navigate(e.Uri);else status.Text="이 창은 외부 소셜 로그인 팝업을 지원하지 않아요. HoYoLAB 이메일 로그인을 이용해 주세요.";};
        c.NavigationCompleted+=async delegate(object s,CoreWebView2NavigationCompletedEventArgs e){if(!closing&&e.IsSuccess)await CheckSession(false);};
        status.Text="아래 공식 HoYoLAB 화면에서 로그인해 주세요. 로그인 완료 시 자동으로 계정을 확인해요. 비밀번호는 앱에서 수집·저장하지 않으며 필요한 세션만 Windows 계정 전용으로 암호화 저장해요.";
        c.Navigate("https://www.hoyolab.com/");poll.Start();
    }catch{if(!closing)status.Text="로그인 창을 열지 못했어요. WebView2 Runtime과 인터넷 연결을 확인해 주세요.";}}
    async Task CheckSession(bool manual){
        if(closing||checking||finished||browser.CoreWebView2==null)return;
        if(DateTime.UtcNow<requestAfter){if(manual)status.Text="인증 확인 요청 간격을 보호하고 있어요. 잠시 기다려 주세요.";return;}
        checking=true;check.Enabled=false;
        try{
            // Read only this app's in-memory official HoYoLAB session cookie scope.
            var list=await browser.CoreWebView2.CookieManager.GetCookiesAsync("https://www.hoyolab.com/");if(closing)return;
            var safe=HoYoLabProvider.FilterCookies(list.Where(c=>c.Domain.TrimStart('.').Equals("hoyolab.com",StringComparison.OrdinalIgnoreCase)||c.Domain.TrimStart('.').Equals("www.hoyolab.com",StringComparison.OrdinalIgnoreCase)).Select(c=>new KeyValuePair<string,string>(c.Name,c.Value)));
            if(!HoYoLabProvider.HasSession(safe)){if(manual)status.Text="아직 로그인 세션을 확인하지 못했어요. 공식 화면에서 로그인을 완료해 주세요.";return;}
            string marker=string.Join("|",safe.OrderBy(c=>c.Key).Select(c=>c.Key+"="+c.Value));
            if(!manual&&marker==previous)return;previous=marker;requestAfter=DateTime.UtcNow.AddSeconds(20);
            status.Text="로그인 세션과 연결된 원신 캐릭터를 확인하고 있어요…";
            var roles=await provider.GetRoles(safe,cancellation.Token);if(closing)return;
            // HTTP success is insufficient: provider validates HoYoLAB retcode and role schema.
            await accept(safe,roles);if(closing)return;finished=true;Close();
        }catch(OperationCanceledException){}catch(HoyoFailure ex){if(!closing){status.Text=ex.Message+" 로그인 후 ‘로그인 확인’을 눌러 다시 시도할 수 있어요.";if(ex.Kind=="rateLimit")requestAfter=DateTime.UtcNow.AddMinutes(15);}}catch{if(!closing)status.Text="로그인 상태를 확인하지 못했어요. 잠시 후 ‘로그인 확인’을 눌러 주세요.";}
        finally{checking=false;if(!closing)check.Enabled=true;}
    }
}
