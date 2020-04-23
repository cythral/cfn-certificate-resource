using System;
using System.Threading.Tasks;

using Cythral.CloudFormation.Resources.Factories;
using NUnit.Framework;

namespace Cythral.CloudFormation.Resources.Tests
{
    public class LambdaFactoryTest
    {
        [SetUp]
        public void SetUpRegionEnvVar()
        {
            Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
        }
        
        [Test]
        public async Task Create_DoesNotReturnNull()
        {
            var factory = new LambdaFactory();
            var result = await factory.Create();

            Assert.That(result, Is.Not.EqualTo(null));
        }
    }
}