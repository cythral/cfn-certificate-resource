using System.Threading.Tasks;

using Amazon.CertificateManager;
using Amazon.SecurityToken.Model;

namespace Cythral.CloudFormation.Resources.Factories
{
    public class AcmFactory
    {
        private StsFactory stsFactory = new StsFactory();

        public virtual async Task<IAmazonCertificateManager> Create(string? roleArn = null)
        {
            if (roleArn != null)
            {
                var stsClient = await stsFactory.Create();
                var response = await stsClient.AssumeRoleAsync(new AssumeRoleRequest
                {
                    RoleArn = roleArn,
                    RoleSessionName = "acm-operations"
                });

                return (IAmazonCertificateManager)new AmazonCertificateManagerClient(response.Credentials);
            }

            return (IAmazonCertificateManager)new AmazonCertificateManagerClient();
        }
    }
}