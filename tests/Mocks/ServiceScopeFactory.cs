using System;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace UnitTests.Mocks
{
    public class ServiceScopeFactory
    {
        public static Mock<IServiceScopeFactory> Get(params (Type @interface, Object service)[] services)
        {
            var scopedServiceProvider = new Mock<IServiceProvider>();

            foreach (var (@interfcae, service) in services)
            {
                scopedServiceProvider
                    .Setup(s => s.GetService(@interfcae))
                    .Returns(service);
            }

            var scope = new Mock<IServiceScope>();
            scope
                .SetupGet(s => s.ServiceProvider)
                .Returns(scopedServiceProvider.Object);

            var serviceScopeFactory = new Mock<IServiceScopeFactory>();
            serviceScopeFactory
                .Setup(x => x.CreateScope())
                .Returns(scope.Object);

            return serviceScopeFactory;
        }
    }
}
