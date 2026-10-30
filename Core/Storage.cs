using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using Microsoft.Win32;

namespace BacSiMayTinh.Core
{
    public static class JsonFile
    {
        private static DataContractJsonSerializer Ser(Type t) =>
            new DataContractJsonSerializer(t, new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });

        public static T Load<T>(string path) where T : class, new()
        {
            try
            {
                if (!File.Exists(path)) return new T();
                using (var fs = File.OpenRead(path))
                    return (T)Ser(typeof(T)).ReadObject(fs) ?? new T();
            }
            catch (Exception ex)
            {
                Logger.Error("Đọc " + path, ex);
                return new T();
            }
        }

        public static void Save<T>(string path, T obj)
        {
            try
            {
                var tmp = path + ".tmp";
                using (var fs = File.Create(tmp)) Ser(typeof(T)).WriteObject(fs, obj);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception ex) { Logger.Error("Ghi " + path, ex); }
        }
    }

    [DataContract]
    public class AppSettings
    {
        [DataMember] public string Theme { get; set; }
        [DataMember] public bool AutoRestorePoint { get; set; }
        [DataMember] public bool MinimizeToTray { get; set; }
        [DataMember] public bool StartWithWindows { get; set; }
        [DataMember] public bool AutoFreeRam { get; set; }
        [DataMember] public int AutoFreeRamThreshold { get; set; }
        [DataMember] public string AutoClean { get; set; }
        [DataMember] public bool? ReduceEffects { get; set; }
        [DataMember] public string Language { get; set; }
        [DataMember] public bool AcceptedTerms { get; set; }
        [DataMember] public bool TasksMigrated { get; set; }
        [DataMember] public bool AutoCheckUpdate { get; set; }
        [DataMember] public string LastUpdateCheck { get; set; }

        // Sao lưu & phục hồi (v1.2)
        [DataMember] public string BackupRoot { get; set; }
        [DataMember] public List<string> BackupRoots { get; set; }
        [DataMember] public string AutoBackup { get; set; }
        [DataMember] public bool AutoBackupDrivers { get; set; }
        [DataMember] public int AutoBackupKeep { get; set; }
        [DataMember] public bool AutoBackupBeforeChange { get; set; }
        [DataMember] public string LastAutoBackup { get; set; }
        [DataMember] public string DataBackupDest { get; set; }
        [DataMember] public List<string> DataBackupSources { get; set; }
        [DataMember] public List<string> DataBackupCustom { get; set; }
        [DataMember] public bool AutoBackupData { get; set; }
        [DataMember] public string LastDataBackup { get; set; }

        public AppSettings() => Defaults();

        [OnDeserializing]
        private void OnDeserializing(StreamingContext c) => Defaults();

        private void Defaults()
        {
            Theme = "dark";
            Language = "vi";
            AutoCheckUpdate = true;
            AutoRestorePoint = true;
            AutoFreeRamThreshold = 85;
            AutoClean = "off";
            AutoBackup = "off";
            AutoBackupKeep = 10;
            AutoBackupBeforeChange = true;
            BackupRoots = new List<string>();
            DataBackupCustom = new List<string>();
        }

        private static AppSettings _current;
        public static AppSettings Current => _current ?? (_current = JsonFile.Load<AppSettings>(FilePath));
        private static string FilePath => Path.Combine(AppInfo.DataDir, "caidat.json");
        public static void SaveCurrent() => JsonFile.Save(FilePath, Current);
    }

    [DataContract]
    public class RegBackup
    {
        [DataMember] public string Path { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public bool Existed { get; set; }
        [DataMember] public string Kind { get; set; }
        [DataMember] public string Data { get; set; }
    }

    /// <summary>Lưu giá trị gốc trước khi tối ưu để có thể hoàn tác.</summary>
    [DataContract]
    public class UndoStore
    {
        [DataMember] public Dictionary<string, List<RegBackup>> Tweaks { get; set; }
        [DataMember] public Dictionary<string, int> Services { get; set; }
        [DataMember] public Dictionary<string, string> Misc { get; set; }

        public UndoStore() => Init();

        [OnDeserialized]
        private void OnDeserialized(StreamingContext c)
        {
            if (Tweaks == null) Tweaks = new Dictionary<string, List<RegBackup>>();
            if (Services == null) Services = new Dictionary<string, int>();
            if (Misc == null) Misc = new Dictionary<string, string>();
        }

        private void Init()
        {
            Tweaks = new Dictionary<string, List<RegBackup>>();
            Services = new Dictionary<string, int>();
            Misc = new Dictionary<string, string>();
        }

        private static readonly object Gate = new object();
        private static UndoStore _current;
        private static string FilePath => System.IO.Path.Combine(AppInfo.BackupDir, "hoantac.json");
        public static UndoStore Current
        {
            get { lock (Gate) return _current ?? (_current = JsonFile.Load<UndoStore>(FilePath)); }
        }
        public static void SaveCurrent() { lock (Gate) JsonFile.Save(FilePath, Current); }

        public static RegBackup Capture(string path, string name)
        {
            var kind = RegistryHelper.GetKind(path, name);
            if (kind == null) return new RegBackup { Path = path, Name = name, Existed = false };
            var v = RegistryHelper.GetValue(path, name);
            return new RegBackup { Path = path, Name = name, Existed = true, Kind = kind.Value.ToString(), Data = RegistryHelper.Encode(v, kind.Value) };
        }

        public static void Restore(RegBackup b)
        {
            if (!b.Existed) { RegistryHelper.DeleteValue(b.Path, b.Name); return; }
            var kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), b.Kind);
            RegistryHelper.SetValue(b.Path, b.Name, RegistryHelper.Decode(b.Data, kind), kind);
        }
    }
}
