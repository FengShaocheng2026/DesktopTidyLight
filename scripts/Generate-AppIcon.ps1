$ErrorActionPreference = 'Stop'
# Run with Windows PowerShell 5.1: powershell.exe -NoProfile -File scripts/Generate-AppIcon.ps1
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
public static class AppIconArtwork {
    static GraphicsPath Round(float x,float y,float w,float h,float r) {
        var p=new GraphicsPath(); float d=r*2;
        p.AddArc(x,y,d,d,180,90); p.AddArc(x+w-d,y,d,d,270,90);
        p.AddArc(x+w-d,y+h-d,d,d,0,90); p.AddArc(x,y+h-d,d,d,90,90); p.CloseFigure(); return p;
    }
    static void Tile(Graphics g,Color color,float x,float y,float w,float h,float r) {
        using(var p=Round(x,y,w,h,r)) using(var b=new SolidBrush(color)) g.FillPath(b,p);
    }
    public static void Generate(string directory) {
        Directory.CreateDirectory(directory);
        int[] sizes={16,20,24,32,40,48,64,128,256};
        var payloads=new byte[sizes.Length][];
        for(int i=0;i<sizes.Length;i++) {
            int n=sizes[i];
            using(var large=new Bitmap(n*4,n*4,PixelFormat.Format32bppArgb)) {
                using(var g=Graphics.FromImage(large)) {
                    g.SmoothingMode=SmoothingMode.AntiAlias; g.ScaleTransform(n*4/256f,n*4/256f);
                    using(var p=Round(8,8,240,240,56))
                    using(var b=new LinearGradientBrush(new Point(24,8),new Point(224,248),Color.FromArgb(116,151,255),Color.FromArgb(83,71,204))) g.FillPath(b,p);
                    // A container with three aligned compartments; no fragile lettering.
                    Tile(g,Color.FromArgb(246,249,255),48,48,160,160,25);
                    Tile(g,Color.FromArgb(85,100,217),64,64,48,128,12);
                    Tile(g,Color.FromArgb(141,160,243),128,64,64,56,12);
                    Tile(g,Color.FromArgb(85,100,217),128,136,64,56,12);
                }
                using(var bitmap=new Bitmap(n,n,PixelFormat.Format32bppArgb)) {
                    using(var g=Graphics.FromImage(bitmap)) { g.InterpolationMode=InterpolationMode.HighQualityBicubic; g.CompositingMode=CompositingMode.SourceCopy; g.DrawImage(large,new Rectangle(0,0,n,n)); }
                    bitmap.Save(Path.Combine(directory,"app-"+n+".png"),ImageFormat.Png);
                    using(var stream=new MemoryStream()) using(var w=new BinaryWriter(stream)) {
                        int maskStride=((n+31)/32)*4;
                        w.Write(40); w.Write(n); w.Write(n*2); w.Write((short)1); w.Write((short)32); w.Write(0); w.Write(n*n*4+maskStride*n);
                        w.Write(0); w.Write(0); w.Write(0); w.Write(0);
                        for(int y=n-1;y>=0;y--) for(int x=0;x<n;x++) { var c=bitmap.GetPixel(x,y); w.Write(c.B); w.Write(c.G); w.Write(c.R); w.Write(c.A); }
                        for(int y=n-1;y>=0;y--) { byte[] row=new byte[maskStride]; for(int x=0;x<n;x++) if(bitmap.GetPixel(x,y).A==0) row[x/8]|=(byte)(128>>(x%8)); w.Write(row); }
                        payloads[i]=stream.ToArray();
                    }
                }
            }
        }
        using(var w=new BinaryWriter(File.Create(Path.Combine(directory,"app.ico")))) {
            w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length); int offset=6+16*sizes.Length;
            for(int i=0;i<sizes.Length;i++) { w.Write((byte)(sizes[i]%256)); w.Write((byte)(sizes[i]%256)); w.Write((short)0); w.Write((short)1); w.Write((short)32); w.Write(payloads[i].Length); w.Write(offset); offset+=payloads[i].Length; }
            foreach(var bytes in payloads) w.Write(bytes);
        }
    }
}
'@
[AppIconArtwork]::Generate((Join-Path $PSScriptRoot '../src/DesktopTidyLight/Assets'))
