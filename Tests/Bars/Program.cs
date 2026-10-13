using NitroPulse;
int cases = 0;
foreach (int size in new[]{16,20,24,32,40,48,64})
foreach (Color accent in new[]{Theme.Cpu,Theme.Gpu,Theme.Ram})
foreach (double percentage in new[]{0,25,50,75,91,99,100})
{
    using var bmp = new Bitmap(size,size);
    using(var graphics=Graphics.FromImage(bmp)) TrayIconRenderer.DrawBar(graphics,size,accent,percentage);
    int border=size<=20?1:2, inner=size-2*border;
    int fillRows=0;
    for(int y=border;y<size-border;y++) if(bmp.GetPixel(size/2,y).ToArgb()==accent.ToArgb()) fillRows++;
    int expected=(int)Math.Floor(inner*percentage/100);
    if(fillRows!=expected || (percentage<100 && fillRows==inner)) throw new Exception($"Incorrect bar {size}px {percentage}%");
    cases++;
}
Console.WriteLine($"PASS: {cases} comprobaciones de píxeles; 91% a 16px deja 2 filas oscuras interiores; 100% llena el interior.");
