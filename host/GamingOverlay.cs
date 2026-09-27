using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

internal sealed class OverlaySettings {
    public bool autoShow=false,themeSync=true;
    public string hotkey="G";
    public string design="pink-capsule";
}
internal sealed class HudTheme {
    internal Color Surface,Bottom,Ink,Accent,Border,Divider,Track;internal int Radius;
    internal static bool Valid(string id){return id=="pink-capsule"||id=="rose-glass"||id=="midnight-lavender";}
    internal static HudTheme For(string id){
        if(id=="rose-glass")return new HudTheme{Surface=Color.FromArgb(72,35,54),Bottom=Color.FromArgb(43,24,37),Ink=Color.FromArgb(255,240,235),Accent=Color.FromArgb(255,178,208),Border=Color.FromArgb(231,133,177),Divider=Color.FromArgb(120,75,100),Track=Color.FromArgb(102,57,82),Radius=20};
        if(id=="midnight-lavender")return new HudTheme{Surface=Color.FromArgb(38,36,49),Bottom=Color.FromArgb(25,25,35),Ink=Color.FromArgb(249,244,255),Accent=Color.FromArgb(211,192,255),Border=Color.FromArgb(177,158,224),Divider=Color.FromArgb(92,84,112),Track=Color.FromArgb(78,68,100),Radius=16};
        return new HudTheme{Surface=Color.FromArgb(250,190,216),Bottom=Color.FromArgb(226,149,190),Ink=Color.FromArgb(65,28,49),Accent=Color.FromArgb(139,34,88),Border=Color.FromArgb(255,222,237),Divider=Color.FromArgb(190,119,158),Track=Color.FromArgb(208,137,174),Radius=28};
    }
    internal static GraphicsPath Round(Rectangle rect,int radius){
        var path=new GraphicsPath();int d=Math.Min(radius*2,Math.Min(rect.Width,rect.Height));
        if(d<2){path.AddRectangle(rect);return path;}
        path.AddArc(rect.Left,rect.Top,d,d,180,90);path.AddArc(rect.Right-d,rect.Top,d,d,270,90);path.AddArc(rect.Right-d,rect.Bottom-d,d,d,0,90);path.AddArc(rect.Left,rect.Bottom-d,d,d,90,90);path.CloseFigure();return path;
    }
    internal static void Clip(Control control,int radius){if(control.Width<2||control.Height<2)return;using(var path=Round(control.ClientRectangle,radius)){var old=control.Region;control.Region=new Region(path);if(old!=null)old.Dispose();}}
}
// The shell knows only menu items and popup content; additional games supply another provider.
internal sealed class HudItem { internal string Id,Text;internal HudItem(string id,string text){Id=id;Text=text;} }
internal sealed class HudPopupData { internal string Title,Body,ImagePath,MainSection;internal double? Progress; }
internal interface IOverlayViewProvider {
    string GameId {get;}
    HudItem[] Items();
    HudPopupData Popup(string id);
}
internal sealed class GenshinOverlayProvider:IOverlayViewProvider {
    readonly GamingService service;
    internal GenshinOverlayProvider(GamingService value){service=value;}
    public string GameId {get{return "genshin";}}
    static string I(int? v){return v.HasValue?v.Value.ToString():"—";}
    static string N(double? v,string unit){return v.HasValue?v.Value.ToString("0.#")+unit:"—";}
    static string Local(string text){DateTime d;return DateTime.TryParse(text,out d)?d.ToLocalTime().ToString("HH:mm"):"확인 불가";}
    public HudItem[] Items(){
        var a=service.Genshin.View;var n=a.notes;
        return new[]{new HudItem("game","◉ 원신"),new HudItem("characters","◇ 캐릭터"),new HudItem("resin","레진 "+(n==null?"—":I(n.resin)+"/"+I(n.resinMax))+(a.stale?" *":"")),new HudItem("expeditions","탐사 "+(n==null?"—":I(n.expeditionCount)+"/"+I(n.expeditionMax))),new HudItem("pc","FPS —  ·  GPU "+N(service.Metrics.temperature,"°C")),new HudItem("hide","×")};
    }
    public HudPopupData Popup(string id){
        var a=service.Genshin.View;var n=a.notes;var m=service.Metrics;
        var p=new HudPopupData{MainSection=id=="characters"?"characters":"genshin"};
        if(id=="resin"){
            p.Title="레진 "+(n==null?"—":I(n.resin)+" / "+I(n.resinMax));
            if(n==null)p.Body="메인 프로그램에서 원신 계정을 연결해 주세요.";
            else{
                DateTime updated;double elapsed=DateTime.TryParse(n.updatedAt,out updated)?Math.Max(0,(DateTime.UtcNow-updated.ToUniversalTime()).TotalSeconds):0;
                double? remain=n.recoverySeconds.HasValue?(double?)Math.Max(0,n.recoverySeconds.Value-elapsed):null;
                p.Body=(remain.HasValue?"완충까지 "+Math.Floor(remain.Value/3600)+"시간 "+Math.Ceiling(remain.Value%3600/60)+"분":"완충시간 확인 불가")+"\n갱신 "+Local(n.updatedAt)+(a.stale?" · 이전 데이터":"");
                if(n.resin.HasValue&&n.resinMax>0)p.Progress=(double)n.resin.Value/n.resinMax.Value;
            }
        }else if(id=="characters"){
            var c=service.Genshin.Characters;var selected=c.detail??c.items.FirstOrDefault(x=>x.id==c.selectedId);
            p.Title=selected==null?"캐릭터 선택":selected.name;
            p.Body=selected==null?"메인 원신 페이지에서 캐릭터를 선택해 주세요.\n기존 캐시만 사용하며 추가 조회하지 않아요.":"Lv."+I(selected.level)+" · "+Element(selected.element)+(c.stale?"\n이전 조회 데이터":"");
            if(selected!=null&&!string.IsNullOrEmpty(selected.icon)){
                Uri url;if(Uri.TryCreate(selected.icon,UriKind.Absolute,out url)&&url.Host=="characters.korugaming.example"){
                    string file=Path.GetFileName(url.AbsolutePath);
                    if(file.Length==68&&file.EndsWith(".png")&&file.Take(64).All(Uri.IsHexDigit)){
                        string path=Path.Combine(service.CharacterImages.Folder,file);if(File.Exists(path))p.ImagePath=path;
                    }
                }
            }
        }else if(id=="expeditions"){
            p.Title="탐사 파견";
            p.Body=n==null?"원신 계정 연결 후 표시돼요.":"파견 "+I(n.expeditionCount)+" / "+I(n.expeditionMax)+"\n완료 "+n.expeditions.Count(x=>x.status=="Finished")+"건\n갱신 "+Local(n.updatedAt)+(a.stale?" · 이전 데이터":"");
        }else if(id=="pc"){
            p.Title="플레이 컨디션";
            p.Body="FPS · 측정 미지원\nGPU "+N(m.temperature,"°C")+" · "+N(m.gpu,"%")+" · "+N(m.power,"W")+"\nCPU "+N(m.cpu,"%")+" · RAM "+N(m.ram,"%")+"\n갱신 "+(m.sampledAt??"확인 대기");p.MainSection=null;
        }else{p.Title="원신";p.Body="Ctrl + "+service.Settings.overlay.hotkey+" · 표시 / 숨김\n메뉴를 클릭하면 작은 팝업이 열려요.\n키보드 포커스는 게임에 유지돼요.";}
        return p;
    }
    static string Element(string e){switch(e){case "Pyro":return "불";case "Hydro":return "물";case "Anemo":return "바람";case "Electro":return "번개";case "Dendro":return "풀";case "Cryo":return "얼음";case "Geo":return "바위";default:return string.IsNullOrEmpty(e)?"원소 미제공":e;}}
}
internal sealed class HudPopup:Form {
    readonly FlowLayoutPanel content=new FlowLayoutPanel{FlowDirection=FlowDirection.TopDown,WrapContents=false,AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,Dock=DockStyle.Fill,Padding=new Padding(16)};
    readonly Label title=new Label{AutoSize=true,Margin=new Padding(0,0,0,10)},body=new Label{AutoSize=true,Margin=new Padding(0,6,0,10)};
    readonly PictureBox portrait=new PictureBox{SizeMode=PictureBoxSizeMode.Zoom,Size=new Size(64,64),Margin=new Padding(0,0,0,6)};
    readonly Panel progress=new Panel{Height=4,Width=264,Margin=new Padding(0,0,0,6)};
    readonly Button link=new Button{Text="메인에서 자세히 보기 ↗",AutoSize=true,FlatStyle=FlatStyle.Flat,Margin=new Padding(0),Padding=new Padding(0,5,0,5),Cursor=Cursors.Hand,TabStop=false};
    Color accent,track;double fraction;string imagePath;internal string MainSection;internal event Action OpenMain;
    internal HudPopup(){
        FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;AutoScaleMode=AutoScaleMode.None;AutoSize=true;AutoSizeMode=AutoSizeMode.GrowAndShrink;Font=new Font("맑은 고딕",14,FontStyle.Regular,GraphicsUnit.Pixel);Padding=new Padding(2);Opacity=.97;
        title.Font=new Font("맑은 고딕",17,FontStyle.Bold,GraphicsUnit.Pixel);title.MaximumSize=new Size(290,0);body.MaximumSize=new Size(290,0);
        link.FlatAppearance.BorderSize=0;link.Click+=delegate{if(OpenMain!=null)OpenMain();};
        progress.Paint+=delegate(object s,PaintEventArgs e){using(var back=new SolidBrush(track))e.Graphics.FillRectangle(back,progress.ClientRectangle);using(var fill=new SolidBrush(accent))e.Graphics.FillRectangle(fill,0,0,(int)(progress.Width*fraction),progress.Height);};
        content.Controls.Add(title);content.Controls.Add(portrait);content.Controls.Add(progress);content.Controls.Add(body);content.Controls.Add(link);Controls.Add(content);
        SizeChanged+=delegate{HudTheme.Clip(this,18);HudTheme.Clip(content,16);};content.SizeChanged+=delegate{HudTheme.Clip(content,16);};
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;return p;}}
    protected override void WndProc(ref Message m){if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
    internal void UpdateData(HudPopupData data,HudTheme theme,int maxWidth){
        accent=theme.Accent;track=theme.Track;BackColor=theme.Border;content.BackColor=theme.Surface;content.ForeColor=theme.Ink;title.ForeColor=theme.Ink;body.ForeColor=theme.Ink;link.BackColor=theme.Surface;link.ForeColor=theme.Accent;link.FlatAppearance.MouseOverBackColor=theme.Bottom;link.FlatAppearance.MouseDownBackColor=theme.Track;
        int width=Math.Min(290,Math.Max(120,maxWidth-34));title.MaximumSize=new Size(width,0);body.MaximumSize=new Size(width,0);progress.Width=width;link.MaximumSize=new Size(width,0);
        title.Text=data.Title;body.Text=data.Body;MainSection=data.MainSection;link.Visible=data.MainSection!=null;
        progress.Visible=data.Progress.HasValue;fraction=Math.Max(0,Math.Min(1,data.Progress??0));progress.Invalidate();
        if(imagePath!=data.ImagePath){imagePath=data.ImagePath;var old=portrait.Image;portrait.Image=null;if(old!=null)old.Dispose();if(imagePath!=null)try{using(var file=Image.FromFile(imagePath))portrait.Image=new Bitmap(file);}catch{}}
        portrait.Visible=portrait.Image!=null;PerformLayout();
    }
    internal bool TextFits {get{return title.PreferredHeight<=title.Height&&body.PreferredHeight<=body.Height;}}
    protected override void Dispose(bool disposing){if(disposing){if(portrait.Image!=null)portrait.Image.Dispose();title.Font.Dispose();Font.Dispose();}base.Dispose(disposing);}
}
internal sealed class GamingOverlay:Form {
    [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr h,int index);
    [DllImport("user32.dll")]static extern int SetWindowLong(IntPtr h,int index,int value);
    [DllImport("user32.dll")]static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
    readonly GamingService service;readonly Action<string> openMain;readonly IOverlayViewProvider provider;
    readonly HudPopup popup=new HudPopup();HudItem[] items=new HudItem[0];Rectangle[] hits=new Rectangle[0];
    bool interactive,wanted,previousRun,preview;string tab="",signature="";int hover=-1;IntPtr gameWindow;
    HudTheme theme=HudTheme.For("pink-capsule");
    internal bool IsShowing {get{return Visible;}}
    internal bool IsInteractive {get{return interactive;}}
    internal GamingOverlay(GamingService data,Action<string> navigate):this(data,navigate,new GenshinOverlayProvider(data)){}
    internal GamingOverlay(GamingService data,Action<string> navigate,IOverlayViewProvider view){
        service=data;openMain=navigate;provider=view;Text="KoruGaming Gaming Bar";FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;TopMost=true;StartPosition=FormStartPosition.Manual;
        AutoScaleMode=AutoScaleMode.None;Font=new Font("맑은 고딕",15,FontStyle.Regular,GraphicsUnit.Pixel);Height=56;Opacity=.95;DoubleBuffered=true;
        SizeChanged+=delegate{HudTheme.Clip(this,theme.Radius);};
        popup.OpenMain+=delegate{string section=popup.MainSection;Suppress();openMain(section);};
        FormClosing+=delegate(object s,FormClosingEventArgs e){if(e.CloseReason==CloseReason.UserClosing){e.Cancel=true;Suppress();}};
    }
    protected override bool ShowWithoutActivation {get{return true;}}
    protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000000|0x80;if(!interactive)p.ExStyle|=0x20;return p;}}
    protected override void WndProc(ref Message m){if(m.Msg==0x21){m.Result=new IntPtr(3);return;}base.WndProc(ref m);}
    void Mode(bool input){interactive=input;if(IsHandleCreated){int style=GetWindowLong(Handle,-20);SetWindowLong(Handle,-20,input?style&~0x20:style|0x20);}if(!input)popup.Hide();}
    internal void Suppress(){wanted=false;preview=false;Hide();popup.Hide();tab="";Mode(false);}
    internal void Toggle(){
        if(wanted){Suppress();return;}
        var game=service.Games.FirstOrDefault(g=>g.id==provider.GameId);if(game==null||!game.running)return;
        previousRun=true;wanted=true;Mode(true);Tick();
    }
    internal void Preview(){preview=true;wanted=true;Mode(true);tab="";Render();Position();Show();}
    internal void EnableAuto(){wanted=true;Mode(false);Tick();}
    internal void Tick(){
        var game=service.Games.FirstOrDefault(g=>g.id==provider.GameId);bool running=game!=null&&game.running;gameWindow=game==null?IntPtr.Zero:game.window;
        if(!preview){
            if(running&&!previousRun){wanted=service.Settings.overlay.autoShow;Mode(false);}
            if(!running)wanted=false;previousRun=running;
            if(!wanted||gameWindow==IntPtr.Zero||IsIconic(gameWindow)||!IsWindowVisible(gameWindow)||GetForegroundWindow()!=gameWindow){Hide();popup.Hide();return;}
        }
        Render();Position();if(wanted&&!Visible)Show();UpdatePopup();
    }
    internal void Render(){
        items=provider.Items();string design=service.Settings.overlay.design;
        string next=string.Join("|",items.Select(i=>i.Text))+"|"+design+"|"+tab+"|"+interactive;
        if(signature!=next){signature=next;theme=HudTheme.For(design);BackColor=theme.Surface;ForeColor=theme.Ink;HudTheme.Clip(this,theme.Radius);AccessibleName=string.Join(" · ",items.Select(i=>i.Text));LayoutItems();Invalidate();}if(popup.Visible)UpdatePopup();
    }
    void LayoutItems(){
        if(items.Length==0)return;
        int[] widths=items.Select(i=>TextRenderer.MeasureText(i.Text,Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+28).ToArray();
        int gap=Math.Max(0,(ClientSize.Width-widths.Sum()-24)/items.Length),x=12;hits=new Rectangle[items.Length];
        for(int i=0;i<items.Length;i++){hits[i]=new Rectangle(x,0,widths[i]+gap,Height);x+=widths[i]+gap;}
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using(var path=HudTheme.Round(new Rectangle(1,1,Width-3,Height-3),theme.Radius-1))using(var fill=new LinearGradientBrush(ClientRectangle,theme.Surface,theme.Bottom,LinearGradientMode.Vertical))using(var border=new Pen(theme.Border,2)){e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(border,path);}
        for(int i=0;i<items.Length&&i<hits.Length;i++){
            Rectangle rect=hits[i];bool selected=tab==items[i].Id;
            TextRenderer.DrawText(e.Graphics,items[i].Text,Font,rect,selected||i==hover?theme.Accent:theme.Ink,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine|TextFormatFlags.VerticalCenter|TextFormatFlags.HorizontalCenter);
            if(i>0)using(var line=new Pen(theme.Divider))e.Graphics.DrawLine(line,rect.Left,20,rect.Left,Height-20);
            if(selected)using(var line=new Pen(theme.Accent,2))e.Graphics.DrawLine(line,rect.Left+20,Height-6,rect.Right-20,Height-6);
        }
    }
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);int index=Array.FindIndex(hits,r=>r.Contains(e.Location));if(index!=hover){hover=index;Cursor=index>=0?Cursors.Hand:Cursors.Default;Invalidate();}}
    protected override void OnMouseLeave(EventArgs e){base.OnMouseLeave(e);hover=-1;Invalidate();}
    protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(!interactive||e.Button!=MouseButtons.Left)return;int index=Array.FindIndex(hits,r=>r.Contains(e.Location));if(index<0)return;if(items[index].Id=="hide"){Suppress();return;}tab=tab==items[index].Id?"":items[index].Id;Render();UpdatePopup();}
    internal static Point BottomCenter(Rectangle area,Size size,int gap){return new Point(area.Left+Math.Max(0,(area.Width-size.Width)/2),Math.Max(area.Top,area.Bottom-size.Height-gap));}
    Rectangle Area {get{return (gameWindow!=IntPtr.Zero?Screen.FromHandle(gameWindow):Screen.FromPoint(Cursor.Position)).Bounds;}}
    void Position(){
        var area=Area;int needed=items.Sum(i=>TextRenderer.MeasureText(i.Text,Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width+28)+24;
        // Physical pixels, independent of monitor DPI: single line is always 56px high.
        int width=Math.Min(area.Width,Math.Max(needed,(int)(area.Width*.60)));
        Size target=new Size(width,56);if(Size!=target){Size=target;LayoutItems();Invalidate();}
        var point=BottomCenter(area,Size,0);if(Location!=point)Location=point;
    }
    void UpdatePopup(){
        if(!Visible||!interactive||tab.Length==0){popup.Hide();return;}
        popup.UpdateData(provider.Popup(tab),theme,Area.Width-16);
        int index=Array.FindIndex(items,i=>i.Id==tab);int anchor=index>=0?Left+hits[index].Left+hits[index].Width/2:Left+Width/2;
        var area=Area;popup.Location=new Point(Math.Max(area.Left+8,Math.Min(anchor-popup.Width/2,area.Right-popup.Width-8)),Math.Max(area.Top,Top-popup.Height-8));
        if(!popup.Visible)popup.Show(this);
    }
    internal string VerifyWindow(string output){
        Preview();bool edge=Bottom==Area.Bottom,height=Height==56,oneLine=hits.Length>0&&hits.Last().Right<=Width;
        using(var bitmap=new Bitmap(Width,Height)){DrawToBitmap(bitmap,new Rectangle(Point.Empty,Size));bitmap.Save(output);}
        tab="resin";UpdatePopup();bool fits=popup.TextFits,above=popup.Bottom<Top;
        using(var bitmap=new Bitmap(popup.Width,popup.Height)){popup.DrawToBitmap(bitmap,new Rectangle(Point.Empty,popup.Size));bitmap.Save(Path.Combine(Path.GetDirectoryName(output),Path.GetFileNameWithoutExtension(output)+"-popup.png"));}
        tab="characters";UpdatePopup();bool characterFits=popup.TextFits;
        using(var bitmap=new Bitmap(popup.Width,popup.Height)){popup.DrawToBitmap(bitmap,new Rectangle(Point.Empty,popup.Size));bitmap.Save(Path.Combine(Path.GetDirectoryName(output),Path.GetFileNameWithoutExtension(output)+"-character.png"));}
        Mode(false);bool through=(GetWindowLong(Handle,-20)&0x20)!=0;Mode(true);bool input=(GetWindowLong(Handle,-20)&0x20)==0;Toggle();bool hidden=!Visible&&!popup.Visible;
        service.Games[0].running=false;Tick();
        return "overlay: rounded="+(Region!=null&&!Region.IsVisible(0,0))+", popupRounded="+(popup.Region!=null&&!popup.Region.IsVisible(0,0))+", edge="+edge+", height56="+height+", singleLineFits="+oneLine+", popupFits="+fits+", characterFits="+characterFits+", popupAbove="+above+", clickThrough="+through+", interactive="+input+", toggleHides="+hidden+", stoppedHidden="+!Visible+", "+OverlayHotkey.VerifyRegistration();
    }
    protected override void Dispose(bool disposing){if(disposing){popup.Dispose();Font.Dispose();}base.Dispose(disposing);}
}
internal sealed class OverlayHotkey:NativeWindow,IDisposable {
    [DllImport("user32.dll",SetLastError=true)]static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint key);
    [DllImport("user32.dll")]static extern bool UnregisterHotKey(IntPtr h,int id);
    readonly Action toggle;internal bool Registered;internal string Message="";
    internal OverlayHotkey(Action callback){toggle=callback;CreateHandle(new CreateParams{Caption="KoruGaming hotkey receiver"});}
    internal static bool Valid(string key){return key!=null&&key.Length==1&&key[0]>='A'&&key[0]<='Z';}
    internal void Bind(string key){UnregisterHotKey(Handle,1);Registered=Valid(key)&&RegisterHotKey(Handle,1,0x4000|2,(uint)key[0]);Message=Registered?"Ctrl + "+key+" · 게임 중 표시/숨김":"Ctrl + "+key+" 단축키를 사용할 수 없습니다. 다른 키를 선택해 주세요.";}
    protected override void WndProc(ref Message m){if(m.Msg==0x312&&m.WParam.ToInt32()==1){toggle();return;}base.WndProc(ref m);}
    internal static string VerifyRegistration(){
        int calls=0;using(var first=new OverlayHotkey(delegate{calls++;}))using(var second=new OverlayHotkey(delegate{})){
            foreach(string candidate in new[]{"Z","Y","X"}){first.Bind(candidate);if(!first.Registered)continue;
                second.Bind(candidate);bool collision=!second.Registered&&second.Message.Contains("사용할 수 없습니다");
                var message=System.Windows.Forms.Message.Create(first.Handle,0x312,new IntPtr(1),IntPtr.Zero);first.WndProc(ref message);
                second.Bind("invalid");return "hotkeyConflict="+collision+", hotkeyDispatch="+(calls==1)+", invalidKeySafe="+!second.Registered;
            }
        }return "hotkeyRegistration=unavailable";
    }
    public void Dispose(){UnregisterHotKey(Handle,1);DestroyHandle();}
}
