using System.Text.Json;

namespace Warehouse;

// Общая папка позволяет нескольким экземплярам программы работать с одним складом.
public class Storage
{
    private readonly string folder;
    private readonly JsonSerializerOptions options = new() { WriteIndented = true };

    public Storage(string folder)
    {
        this.folder = Path.GetFullPath(folder);
        Directory.CreateDirectory(this.folder);
    }

    public Database Load()
    {
        string path = Path.Combine(folder, "data.json");
        if (!File.Exists(path)) return new Database();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return JsonSerializer.Deserialize<Database>(stream) ?? throw new Exception("Файл данных пуст.");
    }

    public void Change(int revision, Action<Database> action)
    {
        // Блокировка и проверка версии защищают от перезаписи чужих изменений.
        using var gate = new FileStream(Path.Combine(folder, "data.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Database data = Load();
        if (data.Revision != revision)
            throw new Exception("Данные изменены другим сотрудником. Нажмите «Обновить» и повторите действие.");
        action(data);
        data.Revision++;
        string path = Path.Combine(folder, "data.json");
        string temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(data, options));
        if (File.Exists(path)) File.Replace(temp, path, null);
        else File.Move(temp, path);
    }

    public static T Copy<T>(T value) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value))!;
}
