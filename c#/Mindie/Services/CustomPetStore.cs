using Mindie.Controls;

namespace Mindie.Services;

public sealed partial class LibraryStore
{
    public List<string> PetWarnings { get; }=[];
    public async Task DeleteCustomPetAsync(string id)
    {
        var pet=Data.CustomPets.SingleOrDefault(p=>p.Id==id)??throw new ArgumentException("Solo puedes eliminar mascotas creadas por ti.");
        var index=Data.CustomPets.IndexOf(pet); var old=Data.Settings.Copy();
        Data.CustomPets.Remove(pet);
        if(Data.Settings.Pet==id) { Data.Settings.Pet="mindie"; Data.Settings.PetName="Mindie"; }
        try { await SaveAsync(); }
        catch { Data.CustomPets.Insert(index,pet); Data.Settings=old; throw; }
        Pets.Unregister(id); SpriteCatalog.UnregisterCustom(id);
        try { File.Delete(PetPath(id)); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { PetWarnings.Add($"{pet.Name} se eliminó de la galería, pero Windows no permitió borrar su archivo PNG local."); }
    }
    public string PetPath(string id)
    {
        if (!id.StartsWith("custom-",StringComparison.Ordinal) || !Guid.TryParseExact(id[7..],"N",out _)) throw new ArgumentException("Identificador de mascota inválido.");
        return Path.Combine(Root,"pets",id+".png");
    }
    private async Task LoadCustomPetsAsync()
    {
        foreach (var pet in Data.CustomPets)
        {
            try { ValidateCustomPet(pet); var path=PetPath(pet.Id); var sheet=await PetImportService.ReadAsync(path); Pets.Register(pet); SpriteCatalog.RegisterCustom(pet.Id,path,sheet.Frames); }
            catch (Exception ex) when (ex is ArgumentException or IOException) { Pets.Unregister(pet.Id); SpriteCatalog.UnregisterCustom(pet.Id); PetWarnings.Add($"No pude cargar {pet.Name}. Vuelve a importar su hoja PNG."); }
        }
        if (Data.Settings.Pet.StartsWith("custom-",StringComparison.Ordinal) && !SpriteCatalog.HasAnimation(Data.Settings.Pet)) { Data.Settings.Pet="mindie"; Data.Settings.PetName="Mindie"; }
    }
    private static void ValidateCustomPet(CustomPet pet)
    {
        pet.Name=pet.Name.Trim(); pet.Description=pet.Description.Trim();
        if (pet.Name.Length is <1 or >24) throw new ArgumentException("Escribe un nombre de 1 a 24 caracteres.");
        if (pet.Description.Length>200) throw new ArgumentException("La descripción puede tener hasta 200 caracteres.");
        if (pet.Description.Length==0) pet.Description="Creado por ti.";
    }
    public async Task<CustomPet> AddCustomPetAsync(string name,string description,ValidatedPetSheet sheet)
    {
        var pet=new CustomPet { Name=name,Description=description }; ValidateCustomPet(pet);
        Directory.CreateDirectory(Path.Combine(Root,"pets")); var path=PetPath(pet.Id);
        await File.WriteAllBytesAsync(path,sheet.Png); Data.CustomPets.Add(pet);
        try { await SaveAsync(); } catch { Data.CustomPets.Remove(pet); File.Delete(path); throw; }
        Pets.Register(pet); SpriteCatalog.RegisterCustom(pet.Id,path,sheet.Frames); return pet;
    }
}
