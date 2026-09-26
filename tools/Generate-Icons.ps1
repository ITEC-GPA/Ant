param([string]$ProjectRoot = (Split-Path $PSScriptRoot -Parent))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;

public sealed class AntIcon : IDisposable
{
    const string Ink = "#293D50", Teal = "#008C82", Gold = "#DC9235", Red = "#C84F58";
    readonly Bitmap bmp = new Bitmap(24,24,PixelFormat.Format32bppArgb);
    readonly Graphics g;
    readonly StringBuilder svg = new StringBuilder("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"24\" height=\"24\" viewBox=\"0 0 24 24\"><g stroke-linecap=\"round\" stroke-linejoin=\"round\">\n");
    static string N(float n) { return n.ToString(CultureInfo.InvariantCulture); }
    public AntIcon() { g=Graphics.FromImage(bmp); g.Clear(Color.Transparent); g.SmoothingMode=SmoothingMode.AntiAlias; }
    void Path(string color, bool fill, params float[] xy)
    {
        var pts=new PointF[xy.Length/2]; var coords=new StringBuilder();
        for(int i=0;i<pts.Length;i++) { pts[i]=new PointF(xy[i*2],xy[i*2+1]); coords.Append(N(pts[i].X)+","+N(pts[i].Y)+" "); }
        using(var p=new Pen(ColorTranslator.FromHtml(color),1.6f))
        { p.StartCap=LineCap.Round; p.EndCap=LineCap.Round; p.LineJoin=LineJoin.Round;
          if(fill) { using(var b=new SolidBrush(p.Color)) g.FillPolygon(b,pts); } else g.DrawLines(p,pts); }
        svg.Append("<"+(fill?"polygon":"polyline")+" points=\""+coords+"\" fill=\""+(fill?color:"none")+"\" stroke=\""+(fill?"none":color)+"\" stroke-width=\"1.6\"/>\n");
    }
    void C(float x,float y,float r,string color,bool fill=false)
    {
        using(var p=new Pen(ColorTranslator.FromHtml(color),1.6f))
        { if(fill) { using(var b=new SolidBrush(p.Color)) g.FillEllipse(b,x-r,y-r,r*2,r*2); } else g.DrawEllipse(p,x-r,y-r,r*2,r*2); }
        svg.Append("<circle cx=\""+N(x)+"\" cy=\""+N(y)+"\" r=\""+N(r)+"\" fill=\""+(fill?color:"none")+"\" stroke=\""+(fill?"none":color)+"\" stroke-width=\"1.6\"/>\n");
    }
    void Box(float x,float y,float w,float h,string color) { Path(color,false,x,y,x+w,y,x+w,y+h,x,y+h,x,y); }
    void Plus(float x,float y,string color) { Path(color,false,x-2.5f,y,x+2.5f,y); Path(color,false,x,y-2.5f,x,y+2.5f); }
    void Arrow(float x,float y,float dx,float dy,string color)
    { Path(color,false,x,y,x+dx,y+dy); float l=(float)Math.Sqrt(dx*dx+dy*dy),a=dx/l*2.5f,b=dy/l*2.5f; Path(color,false,x+dx-a+b,y+dy-b-a,x+dx,y+dy,x+dx-a-b,y+dy-b+a); }
    void Tag() { Path(Ink,false,3,5,11,5,15,9,11,13,3,13,3,5); C(5.8f,8.8f,1,Teal,true); }
    void Document() { Path(Ink,false,4,3,13,3,17,7,17,20,4,20,4,3); Path(Ink,false,13,3,13,7,17,7); Path(Teal,false,7,10,13,10); Path(Teal,false,7,13,11,13); }
    void Funnel() { Path(Ink,false,3,4,20,4,14,11,14,18,10,20,10,11,3,4); }
    void Grid() { Box(3,3,18,18,Ink); Path(Teal,false,3,12,21,12); Path(Teal,false,12,3,12,21); }
    void Refresh() { Path(Teal,false,19,8,16,4,10,3,5,6,3,11); Path(Teal,false,16,8,20,8,20,4); Path(Ink,false,5,16,8,20,14,21,19,18,21,13); Path(Ink,false,8,16,4,16,4,20); }
    void AntMark()
    { Path(Ink,false,9,7,5,4,3,5); Path(Ink,false,15,7,19,4,21,5); Path(Ink,false,8,11,4,10,3,8); Path(Ink,false,16,11,20,10,21,8); Path(Ink,false,8,14,4,15,3,18); Path(Ink,false,16,14,20,15,21,18); Path(Ink,false,9,17,6,20); Path(Ink,false,15,17,18,20); C(12,7,2.5f,Ink,true); C(12,12,2,Teal,true); C(12,17,3,Teal,true); }
    public void Draw(string name)
    {
        switch(name)
        {
        case "PlugInIcon": case "PlugInTabIcon": AntMark(); break;
        case "AddUserDataIcon": Tag(); Plus(17,17,Teal); break;
        case "SetUserDataIcon": Tag(); Arrow(17,20,0,-7,Teal); break;
        case "GetUserDataIcon": Tag(); Arrow(17,13,0,7,Teal); break;
        case "GetUserValuesIcon": Tag(); Path(Teal,false,5,18,8,21,13,16); Path(Gold,false,17,16,21,16); Path(Gold,false,17,20,21,20); break;
        case "UpdateGeometryUserDataIcon": Tag(); Path(Teal,false,4,17,7,20,15,20,19,16,19,11); Path(Teal,false,16,13,19,10,21,13); break;
        case "BakeUserDataIcon": Path(Ink,false,3,11,12,7,21,11,12,15,3,11,3,18,12,21,21,18,21,11); Path(Ink,false,12,15,12,21); Arrow(12,3,0,7,Gold); break;
        case "GetPropertiesIcon": Document(); Path(Gold,false,14,17,21,17); C(18,17,1.7f,Teal,true); break;
        case "FilterPropertiesIcon": Funnel(); Path(Gold,false,5,7,18,7); C(17.5f,17.5f,2.5f,Teal,true); break;
        case "FilterUserDataIcon": Funnel(); C(6.5f,7,1.2f,Teal,true); C(11.5f,7,1.2f,Teal,true); C(16.5f,7,1.2f,Teal,true); break;
        case "GetDocPathIcon": Path(Ink,false,3,8,3,5,9,5,11,8,21,8,19,20,3,20,3,8,21,8); Path(Teal,false,7,16,10,12,13,16,16,12); break;
        case "GetParamDataIcon": Box(3,5,9,14,Ink); C(6,9,1,Teal,true); Path(Teal,false,6,14,9,14); Arrow(13,12,8,0,Teal); break;
        case "ReferenceObjectsIcon": Box(7,7,10,10,Teal); Path(Ink,false,3,8,3,3,8,3); Path(Ink,false,16,3,21,3,21,8); Path(Ink,false,21,16,21,21,16,21); Path(Ink,false,8,21,3,21,3,16); break;
        case "GridDisposeIcon": Box(3,3,7,7,Teal); Box(14,3,7,7,Ink); Box(3,14,7,7,Ink); Box(14,14,7,7,Teal); break;
        case "AutoOrientIcon": Arrow(6,18,0,-14,Ink); Arrow(6,18,14,0,Ink); Path(Teal,false,10,14,14,6,20,9,16,17,10,14); break;
        case "JoinOffsetCurvesIcon": Path(Ink,false,3,18,8,5,15,5,21,18); Path(Teal,false,7,18,11,9,13,9,17,18); Path(Gold,false,3,18,7,18); Path(Gold,false,17,18,21,18); break;
        case "MeshIcon": Grid(); Path(Teal,false,3,3,12,12,21,3); Path(Teal,false,3,21,12,12,21,21); C(12,12,1.8f,Gold,true); break;
        case "MeshRefinementIcon": Grid(); Path(Teal,false,12,3,21,12); Path(Teal,false,12,7.5f,21,7.5f); Path(Teal,false,16.5f,3,16.5f,12); C(16.5f,7.5f,1.8f,Gold,true); break;
        case "CleanMergeMeshIcon": Box(3,7,13,13,Ink); Path(Teal,false,3,7,16,20,3,20,16,7); Plus(18,5,Gold); break;
        case "MeshQualityIcon": Grid(); C(16,16,5,Gold); Path(Teal,false,13,16,15,18,19,13); break;
        case "MeshTopologyIcon": Path(Ink,false,4,4,20,4,20,20,4,20,4,4,20,20); C(4,4,2,Teal,true); C(20,4,2,Teal,true); C(20,20,2,Teal,true); C(4,20,2,Gold,true); break;
        case "MeshPartsIcon": Path(Teal,false,2,4,10,4,10,12,2,4); Path(Ink,false,14,12,22,20,14,20,14,12); Arrow(10,15,-6,5,Gold); Arrow(14,9,6,-5,Gold); break;
        case "MeshExtractFacesIcon": Grid(); Path(Teal,true,12,3,21,3,21,12); Arrow(15,9,6,-6,Gold); break;
        case "MeshNormalsIcon": Path(Ink,false,3,16,12,11,21,16,12,21,3,16); Arrow(12,16,0,-13,Teal); break;
        case "MeshTriangulateIcon": Box(3,3,18,18,Ink); Path(Teal,false,3,3,21,21); C(3,3,2,Gold,true); C(21,21,2,Gold,true); break;
        case "MeshMatchNodesIcon": Grid(); C(12,12,2,Teal,true); C(19,5,2,Gold); Arrow(18,6,-5,5,Gold); break;
        case "MeshMatchFacesIcon": Path(Ink,false,3,6,20,6,20,21,3,6); C(10,13,2,Teal,true); C(10,3,2,Gold); Arrow(10,5,0,6,Gold); break;
        case "CleanGeometryIcon": Path(Ink,false,3,19,6,8,16,5,20,17,3,19); C(3,19,1.7f,Teal,true); C(6,8,1.7f,Teal,true); C(20,17,1.7f,Teal,true); Plus(18,5,Gold); break;
        case "IfIcon": Path(Ink,false,3,12,9,12,15,6,20,6); Path(Teal,false,9,12,15,18,20,18); C(9,12,1.8f,Gold,true); Path(Teal,false,17,3,19,5,21,2.8f); break;
        case "SwitchIcon": C(4,12,1.8f,Ink); C(20,5,1.8f,Ink); C(20,19,1.8f,Teal); Path(Teal,false,7,12,17,18); Path(Ink,false,13,6,16,5); break;
        case "StopIcon": Path(Red,true,8,3,16,3,21,8,21,16,16,21,8,21,3,16,3,8); Path("#FFFFFF",false,8,12,16,12); break;
        case "RecomputeIcon": Refresh(); Path(Gold,true,10,8,16,12,10,16); break;
        case "PersistentCounterIcon": Box(3,5,18,15,Ink); Path(Teal,false,7,9,9,8,9,17); Path(Teal,false,7,17,11,17); Plus(17,12,Gold); break;
        case "SetGenePoolIcon": Path(Ink,false,4,5,20,5); Path(Ink,false,4,12,20,12); Path(Ink,false,4,19,20,19); C(8,5,2,Teal,true); C(16,12,2,Gold,true); C(11,19,2,Teal,true); break;
        case "SubStringIcon": Path(Ink,false,3,9,3,6,21,6,21,9); Path(Teal,false,8,12,8,18,16,18,16,12); Path(Teal,false,8,15,16,15); Path(Gold,false,6,10,6,21); Path(Gold,false,18,10,18,21); break;
        default: throw new ArgumentException(name);
        }
    }
    public void Save(string resources,string name)
    { bmp.Save(PathCombine(resources,name+".png"),ImageFormat.Png); File.WriteAllText(PathCombine(resources,"Vector",name+".svg"),svg.ToString()+"</g></svg>\n"); }
    static string PathCombine(params string[] paths) { return System.IO.Path.Combine(paths); }
    public void Dispose() { g.Dispose(); bmp.Dispose(); }
    public static void Generate(string root)
    {
        string resources=System.IO.Path.Combine(root,"Grasshopper","Resources");
        Directory.CreateDirectory(System.IO.Path.Combine(resources,"Vector"));
        string[] names={"PlugInIcon","PlugInTabIcon","MeshIcon","MeshRefinementIcon","CleanMergeMeshIcon","CleanGeometryIcon","AutoOrientIcon","AddUserDataIcon","SetUserDataIcon","GetUserDataIcon","GetUserValuesIcon","UpdateGeometryUserDataIcon","BakeUserDataIcon","GetPropertiesIcon","FilterPropertiesIcon","FilterUserDataIcon","GetDocPathIcon","GetParamDataIcon","ReferenceObjectsIcon","GridDisposeIcon","JoinOffsetCurvesIcon","IfIcon","SwitchIcon","StopIcon","RecomputeIcon","PersistentCounterIcon","SetGenePoolIcon","SubStringIcon"};
        string[] extra={"MeshQualityIcon","MeshTopologyIcon","MeshPartsIcon","MeshExtractFacesIcon","MeshNormalsIcon","MeshTriangulateIcon","MeshMatchNodesIcon","MeshMatchFacesIcon"};
        int originalCount=names.Length; Array.Resize(ref names,originalCount+extra.Length); Array.Copy(extra,0,names,originalCount,extra.Length);
        using(var sheet=new Bitmap(1000,((names.Length+4)/5)*100)) using(var sg=Graphics.FromImage(sheet)) using(var font=new Font("Segoe UI",9)) using(var small=new Font("Segoe UI",8))
        {
            sg.Clear(Color.FromArgb(246,248,250));
            for(int i=0;i<names.Length;i++)
            {
                using(var icon=new AntIcon()) { icon.Draw(names[i]); icon.Save(resources,names[i]); }
                using(var icon=new Bitmap(System.IO.Path.Combine(resources,names[i]+".png")))
                {
                    if(icon.Width!=24 || icon.Height!=24) throw new Exception("Invalid dimensions");
                    int x=(i%5)*200,y=(i/5)*100;
                    sg.DrawImageUnscaled(icon,x+12,y+20);
                    sg.FillRectangle(Brushes.DarkSlateGray,x+48,y+12,40,40); sg.DrawImageUnscaled(icon,x+56,y+20);
                    sg.InterpolationMode=InterpolationMode.NearestNeighbor; sg.DrawImage(icon,new Rectangle(x+110,y+6,60,60));
                    sg.DrawString(names[i].Replace("Icon",""),font,Brushes.DarkSlateGray,x+10,y+72);
                }
            }
            Directory.CreateDirectory(System.IO.Path.Combine(root,"tools"));
            sheet.Save(System.IO.Path.Combine(root,"tools","icons-preview.png"),ImageFormat.Png);
        }
    }
}
'@
[AntIcon]::Generate($ProjectRoot)
Write-Output 'Generated 36 transparent 24x24 PNG icons, editable SVG sources and preview sheet.'
