using System.Buffers.Binary;
using System.Security.Cryptography;
using Mindie.Controls;

namespace Mindie.Services;

public sealed record ValidatedPetSheet(byte[] Png,BitmapSource[,] Frames);
public static class PetImportService
{
    public static async Task<ValidatedPetSheet> ReadAsync(string path,CancellationToken cancellation=default)
    {
        if (!File.Exists(path) || new FileInfo(path).Length>25_000_000) throw new ArgumentException("Elige una hoja PNG de hasta 25 MB.");
        return await ValidateAsync(await File.ReadAllBytesAsync(path,cancellation),cancellation);
    }
    public static Task<ValidatedPetSheet> ValidateAsync(byte[] bytes,CancellationToken cancellation=default)
    {
        if (bytes.Length<24 || bytes.Length>25_000_000 || !bytes.AsSpan(0,8).SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) throw new ArgumentException("Importa el archivo PNG original de ChatGPT, con transparencia.");
        var width=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(16,4)); var height=BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(20,4));
        if (width<256 || width>2048 || width%4!=0 || height%6!=0 || width/4!=height/6) throw new ArgumentException("La hoja debe tener 4 columnas y 6 filas de celdas cuadradas; por ejemplo 1024 × 1536 px.");
        var completion=new TaskCompletionSource<ValidatedPetSheet>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker=new Thread(()=> {
            try
            {
                cancellation.ThrowIfCancellationRequested(); var image=Images.Decode(bytes); var rgba=new FormatConvertedBitmap(image,PixelFormats.Bgra32,null,0);
                var pixels=new byte[width*height*4]; rgba.CopyPixels(pixels,width*4,0); var transparent=0;
                for (var index=3;index<pixels.Length;index+=4) if (pixels[index]<12) transparent++;
                if (transparent<width*height/10) throw new ArgumentException("La imagen no tiene suficiente transparencia real. Pide a ChatGPT un PNG con alfa, sin fondo blanco ni cuadrícula dibujada.");
                var cell=width/4; var inset=Math.Max(2,cell/20);
                for(var row=0;row<6;row++) for(var column=0;column<4;column++)
                {
                    var clear=0;
                    foreach(var point in new[] { (inset,inset),(cell-1-inset,inset),(inset,cell-1-inset),(cell-1-inset,cell-1-inset) }) if(pixels[((row*cell+point.Item2)*width+column*cell+point.Item1)*4+3]<12) clear++;
                    if(clear<3) throw new ArgumentException($"La celda {row+1}, {column+1} tiene fondo o invadió sus márgenes. Pide el personaje completo con espacio transparente alrededor.");
                }
                var frames=SpriteCatalog.PrepareFrames(image,cancellation);
                for (var row=0;row<6;row++)
                {
                    var variants=new HashSet<string>();
                    for (var column=0;column<4;column++) { var frame=frames[row,column]; var data=new byte[frame.PixelWidth*frame.PixelHeight*4]; frame.CopyPixels(data,frame.PixelWidth*4,0); variants.Add(Convert.ToHexString(SHA256.HashData(data))); }
                    if (variants.Count<2) throw new ArgumentException($"La fila {row+1} repite la misma pose. Pide cuatro poses consecutivas diferentes para cada estado.");
                }
                completion.SetResult(new(bytes,frames));
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellation); }
            catch (Exception ex) { completion.TrySetException(ex is ArgumentException ? ex : new ArgumentException("No pude leer las 24 poses. Comprueba la cuadrícula, los márgenes y la transparencia del PNG.",ex)); }
        }) { IsBackground=true };
        worker.SetApartmentState(ApartmentState.STA); worker.Start(); return completion.Task;
    }
    public static string Prompt(string idea) => $$"""
Genera UNA imagen PNG para una mascota de escritorio de Mindie.
Idea del personaje: {{(string.IsNullOrWhiteSpace(idea)?"un compañero original, amistoso y de estilo neutro":idea.Trim())}}

La entrega es una HOJA DE SPRITES animados, no una ilustración individual. PNG RGBA, fondo con transparencia alfa real, orientación vertical 2:3, idealmente 1024×1536 px. Exactamente 4 columnas por 6 filas de celdas CUADRADAS iguales: 24 poses del MISMO personaje. Mantén idénticos diseño, colores, proporciones, iluminación, escala, centro horizontal y línea de apoyo en todas las poses. Personaje completo y accesorios dentro de cada celda con 15% de margen. Nada debe tocar otra celda. Sin líneas de cuadrícula, etiquetas, texto, suelo, sombras de fondo ni símbolos flotantes. La transparencia no debe ser una cuadrícula pintada.

Cada fila contiene cuatro poses CONSECUTIVAS distintas, de izquierda a derecha:
1. REPOSO/PARPADEO: ojos abiertos, medio cerrados, cerrados, abriéndose.
2. MOVIMIENTO: inclinación a la izquierda, giro a la derecha con gesto, pequeño salto, volver al apoyo.
3. PENSANDO: mirar arriba a la izquierda, mano o ala en la mejilla, mirar arriba a la derecha, gesto pensativo.
4. CON SUEÑO: párpados caídos, bostezo, cabeza inclinada, cabecear hacia abajo.
5. DORMIDO: postura acurrucada con ojos cerrados, inspiración suave, espiración, postura relajada. Cambios sutiles pero reales.
6. COMER/BEBER: sujetar un alimento o taza adecuado al personaje, morder o beber, masticar o tragar, bajar el alimento o taza. El accesorio toca la mano, ala o cuerpo para formar una silueta conectada.

Los ojos y el cuerpo deben cambiar realmente entre poses. No repitas 24 veces una imagen estática. Todas las extremidades y accesorios deben quedar enteros. No añadas ZZZ, corazones, bocadillos ni otros elementos separados del personaje. Usa un estilo visual coherente con mi idea, amable y legible cuando la mascota mida 144 px.

Antes de entregar, comprueba que hay 24 poses, 4 columnas, 6 filas, celdas cuadradas y fondo alfa real. Entrega la imagen PNG para descargar. Si la salida no tiene la cuadrícula o la transparencia solicitadas, corrígela antes de darla por terminada.
""";
}
