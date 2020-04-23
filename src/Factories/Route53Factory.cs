using System.Threading.Tasks;

using Amazon.Route53;
using Amazon.SecurityToken.Model;

namespace Cythral.CloudFormation.Resources.Factories
{
    public class Route53Factory
    {
        private StsFactory stsFactory = new StsFactory();

        public virtual async Task<IAmazonRoute53> Create(string? roleArn = null)
        {
            if (roleArn != null)
            {
                var stsClient = await stsFactory.Create();
                var response = await stsClient.AssumeRoleAsync(new AssumeRoleRequest
                {
                    RoleArn = roleArn,
                    RoleSessionName = "route-53-operations"
                });

                return (IAmazonRoute53)new AmazonRoute53Client(response.Credentials);
            }

            return (IAmazonRoute53)new AmazonRoute53Client();
        }
    }
}