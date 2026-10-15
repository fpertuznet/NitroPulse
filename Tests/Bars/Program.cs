using NitroPulse;
int cases = 0;
foreach (int size in new[]{16,20,24,32,40,48,64})
foreach (Color accent in new[]{Theme.Cpu,Theme.Gpu,Theme.Ram})
foreach (double percentage in new[]{0,25,50,75,91,99,99.99})
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
Console.WriteLine($"PASS: {cases} comprobaciones de barras por debajo de 100%; 91% a 16px deja 2 filas oscuras interiores.");
int warnings=0;
foreach(int size in new[]{16,20,24,32,40,48,64})
foreach(Color accent in new[]{Theme.Cpu,Theme.Gpu,Theme.Ram})
foreach(double percentage in new[]{100d,100.01,150})
{
    using var bmp=new Bitmap(size,size);
    using(var graphics=Graphics.FromImage(bmp)) TrayIconRenderer.DrawBar(graphics,size,accent,percentage);
    int gapRows=0,ink=0;
    for(int y=0;y<size;y++) {
        if(bmp.GetPixel(size/2,y).A==0)gapRows++;
        for(int x=0;x<size;x++) {var color=bmp.GetPixel(x,y);if(color.A>0){if(color.ToArgb()!=accent.ToArgb())throw new Exception("Exclamación con color incorrecto");ink++;}}
    }
    if(gapRows<2 || ink==0 || bmp.GetPixel(0,0).A!=0 || bmp.GetPixel(size/2,size-1).ToArgb()!=accent.ToArgb()) throw new Exception("No se dibujó una exclamación transparente con punto separado");
    warnings++;
}
foreach(double? value in new double?[]{null,double.NaN,double.PositiveInfinity,-1}) {
    using var bmp=new Bitmap(16,16);
    using(var graphics=Graphics.FromImage(bmp))TrayIconRenderer.DrawBar(graphics,16,Theme.Cpu,value);
    if(bmp.GetPixel(0,0).A==0)throw new Exception("Dato desconocido convertido en alerta");
}
if(TrayIconRenderer.CacheKey(TrayMetricKind.VerticalBar,99.6,"100")==TrayIconRenderer.CacheKey(TrayMetricKind.VerticalBar,100,"100"))throw new Exception("La caché impide activar o desactivar la alerta");
if(TrayIconRenderer.CacheKey(TrayMetricKind.TemperatureNumber,50,"50")!="50")throw new Exception("Temperatura modificada");
if(TrayIconRenderer.PercentText(99.999)=="100" || TrayIconRenderer.PercentText(100)!="100")throw new Exception("Tooltip redondea una barra incompleta a 100%");
Console.WriteLine($"PASS: {warnings} exclamaciones a 100% y superiores; colores, tamaños y transición 99.6 ↔ 100 correctos.");
var viewport=new PlotViewport(300);
long[] times=Enumerable.Range(0,1000).Select(i=>(long)i).ToArray();
viewport.Update(times);
if(viewport.Start!=700 || viewport.Count!=300 || !viewport.IsLive)throw new Exception("Inicio en vivo incorrecto");
viewport.ScrollTo(100,times);
viewport.Update(Enumerable.Range(0,1030).Select(i=>(long)i).ToArray());
if(viewport.Start!=100 || viewport.IsLive)throw new Exception("Las muestras nuevas arrastran la vista histórica");
viewport.Update(Enumerable.Range(150,1000).Select(i=>(long)i).ToArray());
if(viewport.Start!=0 || viewport.IsLive)throw new Exception("Historial eliminado fuera de límites");
viewport.Live(times);
if(viewport.Start!=700 || !viewport.IsLive)throw new Exception("En vivo no vuelve al final");
viewport.ScrollTo(int.MaxValue,times);
if(!viewport.IsLive || viewport.Start!=700)throw new Exception("Límite derecho incorrecto");
viewport.ScrollTo(-50,times);
if(viewport.Start!=0 || viewport.IsLive)throw new Exception("Límite izquierdo incorrecto");
viewport.Update(Array.Empty<long>());
if(viewport.Count!=0 || viewport.Start!=0)throw new Exception("Historial vacío incorrecto");
Console.WriteLine("PASS: navegación por muestras, límites, anclaje ante nuevos datos y recorte, retorno a vivo y vacío.");
