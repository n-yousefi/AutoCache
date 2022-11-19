using System.Collections.Generic;
using System.Threading.Tasks;
using UnitTests.Mocks;

namespace UnitTests.Services
{
    public class ToDoService : IToDoService
    {
        public ToDoService()
        {
            States = new Dictionary<string, int>();
            Access = new Dictionary<string, int>();
        }
        public Dictionary<string, int> States { get; set; }
        public Dictionary<string, int> Access { get; set; }
        public void Set(string key, int value)
        {
            States[key] = value;
        }
        public virtual Task<int> GetAsync(string key)
        {
            if (!Access.ContainsKey(key))
                Access[key] = 0;
            Access[key]++;
            return Task.FromResult(States[key]);
        }

        public int AccessCount(string key)
        {
            return Access[key];
        }

    }
}
