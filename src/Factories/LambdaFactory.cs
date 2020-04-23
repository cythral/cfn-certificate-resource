using System.Threading.Tasks;

using Amazon.Lambda;

namespace Cythral.CloudFormation.Resources.Factories
{
    public class LambdaFactory
    {
        public virtual Task<IAmazonLambda> Create()
        {
            return Task.FromResult((IAmazonLambda)new AmazonLambdaClient());
        }
    }
}