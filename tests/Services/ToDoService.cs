using System.Threading.Tasks;
using UnitTests.Mocks;

namespace UnitTests.Services
{
    public class ToDoService : IToDoService
    {
        public virtual Task<int> GetAsync() => Task.FromResult(Db.State);
    }
}
