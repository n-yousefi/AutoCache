using System.Threading.Tasks;
using UnitTests.Mocks;

namespace UnitTests.Services
{
    public class ToDoService : IToDoService
    {
        public virtual Task<int> GetAsync(string key) => Task.FromResult(Db.State);
    }
}
