using System.Threading.Tasks;

namespace UnitTests.Services
{
    public interface IToDoService
    {
        Task<int> GetAsync(string key);
    }
}
