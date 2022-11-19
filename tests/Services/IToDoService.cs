using System.Threading.Tasks;

namespace UnitTests.Services
{
    public interface IToDoService
    {
        void Set(string key, int value);
        Task<int> GetAsync(string key);
    }
}
