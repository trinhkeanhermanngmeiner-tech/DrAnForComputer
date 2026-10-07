using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace BacSiMayTinh.Core
{
    public abstract class ObservableObject : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        protected void OnPropertyChanged([CallerMemberName] string name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public enum Risk { Safe, Moderate, Advanced }

    public static class RiskInfo
    {
        public static string Text(Risk r) => L.T(r == Risk.Safe ? "An toàn" : r == Risk.Moderate ? "Cẩn thận" : "Chuyên gia");
    }
}
