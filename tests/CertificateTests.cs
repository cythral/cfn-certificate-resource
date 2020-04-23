using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

using Amazon.CertificateManager;
using Amazon.CertificateManager.Model;
using Amazon.Lambda;
using Amazon.Lambda.Core;
using Amazon.Lambda.Model;
using Amazon.Route53;
using Amazon.Route53.Model;

using Cythral.CloudFormation.CustomResource.Core;
using Cythral.CloudFormation.CustomResource.Attributes;
using Cythral.CloudFormation.Resources;
using Cythral.CloudFormation.Resources.Factories;
using static Cythral.CloudFormation.Resources.Tests.TestUtils;

using FluentAssertions;

using NSubstitute;

using NUnit.Framework;

using RichardSzalay.MockHttp;

using Tag = Amazon.CertificateManager.Model.Tag;
using ResourceRecord = Amazon.CertificateManager.Model.ResourceRecord;


namespace Cythral.CloudFormation.Resources.Tests
{
    public class CertificateTests
    {
        private static string FUNCTION_NAME = "Certificate";
        private static string DOMAIN_NAME = "example.com";
        private static string ALTERNATIVE_NAME = "www.example.com";
        private static string HOSTED_ZONE_ID = "ABC123";
        private static string ARN = $"arn:aws:acm::1:certificate/{DOMAIN_NAME}";
        private static List<DomainValidation> VALIDATION_OPTIONS = new List<DomainValidation> {
            new DomainValidation {
                DomainName = DOMAIN_NAME,
                ResourceRecord = new ResourceRecord {
                    Name = $"_x1.{DOMAIN_NAME}",
                    Type = RecordType.CNAME,
                    Value = "example-com.acm-validations.aws"
                }
            },
            new DomainValidation {
                DomainName = ALTERNATIVE_NAME,
                ResourceRecord = new ResourceRecord {
                    Name = $"_x2.{ALTERNATIVE_NAME}",
                    Type = RecordType.CNAME,
                    Value = "www-example-com.acm-validations.aws"
                }
            },
        };
        private static List<Tag> TAGS = new List<Tag> {
            new Tag {
                Key = "Contact",
                Value = "Talen Fisher"
            },
            new Tag {
                Key = "ContactEmail",
                Value = "talen@example.com"
            }
        };
        private static Certificate.Properties DEFAULT_PROPS = new Certificate.Properties
        {
            DomainName = DOMAIN_NAME,
            HostedZoneId = HOSTED_ZONE_ID,
            ValidationMethod = ValidationMethod.DNS,
            Tags = TAGS,
            SubjectAlternativeNames = new List<string> { ALTERNATIVE_NAME },
        };

        private Certificate CreateInstance(
            Certificate.Properties? props = null,
            IAmazonCertificateManager? acmClient = null,
            IAmazonRoute53? route53Client = null,
            IAmazonLambda? lambdaClient = null,
            string? functionName = null)
        {
            Route53Factory route53Factory;
            AcmFactory acmFactory;
            LambdaFactory lambdaFactory;

            return CreateInstance(out acmFactory, out route53Factory, out lambdaFactory, props, acmClient, route53Client, lambdaClient, functionName);
        }

        private Certificate CreateInstance(
            out AcmFactory acmFactory,
            out Route53Factory route53Factory,
            out LambdaFactory lambdaFactory,
            Certificate.Properties? props = null,
            IAmazonCertificateManager? acmClient = null,
            IAmazonRoute53? route53Client = null,
            IAmazonLambda? lambdaClient = null,
            string? functionName = null)
        {
            var certificate = new Certificate
            {
                PhysicalResourceId = ARN,
                Request = new Request<Certificate.Properties>
                {
                    PhysicalResourceId = ARN,
                    ResourceProperties = props ?? DEFAULT_PROPS
                },
            };
            route53Factory = CreateRoute53Factory(route53Client);
            acmFactory = CreateAcmFactory(acmClient);
            lambdaFactory = CreateLambdaFactory(lambdaClient);

            var context = Substitute.For<ILambdaContext>();
            context.FunctionName.Returns(functionName ?? FUNCTION_NAME);

            SetReadonlyField(certificate, "Context", context);
            SetPrivateField(certificate, "route53Factory", route53Factory);
            SetPrivateField(certificate, "acmFactory", acmFactory);
            SetPrivateField(certificate, "lambdaFactory", lambdaFactory);
            return certificate;
        }

        private Route53Factory CreateRoute53Factory(IAmazonRoute53? client = null)
        {
            var factory = Substitute.For<Route53Factory>();
            client = client ?? CreateRoute53Client();
            factory.Create(Arg.Any<string>()).Returns(client);
            return factory;
        }

        private AcmFactory CreateAcmFactory(IAmazonCertificateManager? client = null)
        {
            var factory = Substitute.For<AcmFactory>();
            client = client ?? CreateAcmClient();
            factory.Create(Arg.Any<string>()).Returns(client);
            return factory;
        }

        private LambdaFactory CreateLambdaFactory(IAmazonLambda? client = null)
        {
            var factory = Substitute.For<LambdaFactory>();
            client = client ?? CreateLambdaClient();
            factory.Create().Returns(client);
            return factory;
        }

        public IAmazonCertificateManager CreateAcmClient(string? domainName = null, string? arn = null, List<DomainValidation>? validationOptions = null)
        {
            domainName = domainName ?? DOMAIN_NAME;
            arn = arn ?? ARN;
            validationOptions = validationOptions ?? VALIDATION_OPTIONS;

            var client = Substitute.For<IAmazonCertificateManager>();

            client
            .RequestCertificateAsync(Arg.Any<RequestCertificateRequest>())
            .Returns(new RequestCertificateResponse
            {
                CertificateArn = ARN
            });

            client
            .DescribeCertificateAsync(Arg.Is<DescribeCertificateRequest>(req =>
                req.CertificateArn == ARN
            ))
            .Returns(new DescribeCertificateResponse
            {
                Certificate = new CertificateDetail
                {
                    Status = CertificateStatus.ISSUED, // don't test wait
                    DomainValidationOptions = VALIDATION_OPTIONS
                }
            });

            client
            .AddTagsToCertificateAsync(Arg.Any<AddTagsToCertificateRequest>())
            .Returns(new AddTagsToCertificateResponse { });

            client
            .RemoveTagsFromCertificateAsync(Arg.Any<RemoveTagsFromCertificateRequest>())
            .Returns(new RemoveTagsFromCertificateResponse { });

            client
            .UpdateCertificateOptionsAsync(Arg.Any<UpdateCertificateOptionsRequest>())
            .Returns(new UpdateCertificateOptionsResponse { });

            client
            .DeleteCertificateAsync(Arg.Any<DeleteCertificateRequest>())
            .Returns(new DeleteCertificateResponse { });

            return client;
        }

        public IAmazonRoute53 CreateRoute53Client()
        {
            var client = Substitute.For<IAmazonRoute53>();

            client
            .ChangeResourceRecordSetsAsync(Arg.Any<ChangeResourceRecordSetsRequest>())
            .Returns(new ChangeResourceRecordSetsResponse { });

            return client;
        }

        public IAmazonLambda CreateLambdaClient()
        {
            var client = Substitute.For<IAmazonLambda>();

            client
            .InvokeAsync(Arg.Any<InvokeRequest>())
            .Returns(new InvokeResponse { });

            return client;
        }

        [SetUp]
        public void SetUpCertificate()
        {
            Certificate.WaitInterval = 0;
        }
        
        [SetUp]
        public void SetUpRegionEnvVar()
        {
            System.Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
        }

        [Test]
        public async Task Create_CallsRequestCertificate()
        {
            var client = CreateAcmClient();
            var instance = CreateInstance(acmClient: client);

            await instance.Create();
            await client.Received().RequestCertificateAsync(
                Arg.Is<RequestCertificateRequest>(req =>
                    req.DomainName == DOMAIN_NAME &&
                    req.SubjectAlternativeNames.Any(name => name == ALTERNATIVE_NAME) &&
                    req.ValidationMethod == ValidationMethod.DNS
                )
            );
        }

        [Test]
        public async Task Create_AddsTags()
        {
            var client = CreateAcmClient();
            var instance = CreateInstance(acmClient: client);

            await instance.Create();
            await client.Received().AddTagsToCertificateAsync(Arg.Is<AddTagsToCertificateRequest>(req =>
                req.Tags.Any(tag => tag.Key == "Contact" && tag.Value == "Talen Fisher") &&
                req.Tags.Any(tag => tag.Key == "ContactEmail" && tag.Value == "talen@example.com") &&
                req.CertificateArn == ARN
            ));
        }

        [Test]
        public void Create_PhysicalResourceIdShouldBeSetBeforeDescribeFails()
        {
            var client = CreateAcmClient();

            client
            .When(x => x.DescribeCertificateAsync(Arg.Any<DescribeCertificateRequest>()))
            .Do(x => { throw new Exception(); });

            var instance = CreateInstance(acmClient: client);

            Assert.ThrowsAsync<Exception>(async () => await instance.Create());
            Assert.That(instance.PhysicalResourceId, Is.Not.EqualTo(null));
        }

        [Test]
        public async Task Create_CallsChangeResourceRecordSets()
        {
            var client = CreateRoute53Client();
            var instance = CreateInstance(route53Client: client);

            await instance.Create();
            await client.Received().ChangeResourceRecordSetsAsync(
                Arg.Is<ChangeResourceRecordSetsRequest>(req =>
                    req.HostedZoneId == HOSTED_ZONE_ID &&
                    req.ChangeBatch.Changes.Any(change =>
                        change.Action == ChangeAction.UPSERT &&
                        change.ResourceRecordSet.Type == RRType.CNAME &&
                        change.ResourceRecordSet.Name == "_x1.example.com" &&
                        change.ResourceRecordSet.ResourceRecords.Any(record =>
                            record.Value == "example-com.acm-validations.aws"
                        )
                    )
                )
            );
        }

        [Test]
        public async Task Create_WithCreationRoleArn_CallsCreateAcmFactoryWithRoleArn()
        {
            AcmFactory acmFactory;
            Route53Factory route53Factory;
            LambdaFactory lambdaFactory;

            var instance = CreateInstance(out acmFactory, out route53Factory, out lambdaFactory);
            var roleArn = "arn";
            instance.Request.ResourceProperties.CreationRoleArn = roleArn;

            await instance.Create();
            await acmFactory.Received().Create(Arg.Is<string>(val => val == roleArn));
        }

        [Test]
        public async Task Create_WithValidationRoleArn_CallsCreateRoute53FactoryWithRoleArn()
        {
            AcmFactory acmFactory;
            Route53Factory route53Factory;
            LambdaFactory lambdaFactory;

            var instance = CreateInstance(out acmFactory, out route53Factory, out lambdaFactory);
            var roleArn = "arn";
            instance.Request.ResourceProperties.ValidationRoleArn = roleArn;

            await instance.Create();
            await route53Factory.Received().Create(Arg.Is<string>(val => val == roleArn));
        }

        [Test]
        public async Task Wait_WithCreationRoleArn_CallsCreateAcmFactoryWithRoleArn()
        {
            AcmFactory acmFactory;
            Route53Factory route53Factory;
            LambdaFactory lambdaFactory;

            var instance = CreateInstance(out acmFactory, out route53Factory, out lambdaFactory);
            var roleArn = "arn";
            instance.Request.ResourceProperties.CreationRoleArn = roleArn;

            await instance.Wait();
            await acmFactory.Received().Create(Arg.Is<string>(val => val == roleArn));
        }

        [Test]
        public async Task Wait_CallsInvokeIfStatusIsPending()
        {
            var acmClient = CreateAcmClient();
            var lambdaClient = CreateLambdaClient();

            acmClient
            .DescribeCertificateAsync(Arg.Any<DescribeCertificateRequest>())
            .Returns(new DescribeCertificateResponse
            {
                Certificate = new CertificateDetail
                {
                    Status = CertificateStatus.PENDING_VALIDATION
                }
            });

            var instance = CreateInstance(acmClient: acmClient, lambdaClient: lambdaClient);
            var options = new JsonSerializerOptions();
            options.Converters.Add(new JsonStringEnumConverter());
            options.Converters.Add(new AwsConstantClassConverterFactory());
            string expectedPayload = JsonSerializer.Serialize(instance.Request, options);

            await instance.Wait();
            await lambdaClient.Received().InvokeAsync(
                Arg.Is<InvokeRequest>(req =>
                    req.FunctionName == FUNCTION_NAME &&
                    req.Payload == expectedPayload &&
                    req.InvocationType == InvocationType.Event
                )
            );
        }

        [Test]
        public async Task Wait_DoesNotCallInvokeWhenCertIsIssued()
        {
            var acmClient = CreateAcmClient();
            var lambdaClient = CreateLambdaClient();
            var instance = CreateInstance(acmClient: acmClient, lambdaClient: lambdaClient);
            instance.PhysicalResourceId = ARN;

            await instance.Wait();
            await lambdaClient.DidNotReceive().InvokeAsync(Arg.Any<InvokeRequest>());
        }

        [Test]
        public void Wait_ThrowsIfValidationTimedOut()
        {
            var acmClient = CreateAcmClient();

            acmClient
            .DescribeCertificateAsync(Arg.Any<DescribeCertificateRequest>())
            .Returns(new DescribeCertificateResponse
            {
                Certificate = new CertificateDetail
                {
                    Status = CertificateStatus.VALIDATION_TIMED_OUT
                }
            });

            var instance = CreateInstance(acmClient: acmClient);
            Assert.ThrowsAsync<Exception>(async () => await instance.Wait());
        }

        [Test]
        public async Task Update_WithCreationRoleArn_CallsCreateAcmFactoryWithRoleArn()
        {
            AcmFactory acmFactory;
            Route53Factory route53Factory;
            LambdaFactory lambdaFactory;

            var instance = CreateInstance(out acmFactory, out route53Factory, out lambdaFactory);
            var roleArn = "arn";
            instance.Request.ResourceProperties.CreationRoleArn = roleArn;

            await instance.Update();
            await acmFactory.Received().Create(Arg.Is<string>(val => val == roleArn));
        }

        [Test]
        public async Task Update_CallsAddAndDeleteTags()
        {
            var acmClient = CreateAcmClient();
            var route53Client = CreateRoute53Client();
            var instance = CreateInstance(acmClient: acmClient, route53Client: route53Client);
            instance.Request.PhysicalResourceId = ARN;
            instance.Request.OldResourceProperties = instance.Request.ResourceProperties;
            instance.Request.ResourceProperties = new Certificate.Properties
            {
                DomainName = DOMAIN_NAME,
                HostedZoneId = HOSTED_ZONE_ID,
                Tags = new List<Tag> {
                    new Tag {
                        Key = "Contact",
                        Value = "Someone else"
                    },

                }
            };

            await instance.Update();
            await acmClient.Received().AddTagsToCertificateAsync(Arg.Is<AddTagsToCertificateRequest>(req =>
                req.Tags.Any(tag => tag.Key == "Contact" && tag.Value == "Someone else") &&
                req.CertificateArn == ARN
            ));

            await acmClient.Received().RemoveTagsFromCertificateAsync(Arg.Is<RemoveTagsFromCertificateRequest>(req =>
                req.Tags.Any(tag => tag.Key == "ContactEmail" && tag.Value == "talen@example.com") &&
                req.CertificateArn == ARN
            ));
        }

        [Test]
        public async Task Update_CallsUpdateOptions()
        {
            var acmClient = CreateAcmClient();
            var route53Client = CreateRoute53Client();
            var instance = CreateInstance(acmClient: acmClient, route53Client: route53Client);
            instance.Request.PhysicalResourceId = ARN;
            instance.Request.OldResourceProperties = instance.Request.ResourceProperties;
            instance.Request.ResourceProperties = new Certificate.Properties
            {
                Options = new CertificateOptions
                {
                    CertificateTransparencyLoggingPreference = CertificateTransparencyLoggingPreference.ENABLED
                }
            };

            await instance.Update();
            await acmClient.Received().UpdateCertificateOptionsAsync(Arg.Is<UpdateCertificateOptionsRequest>(req =>
                req.CertificateArn == ARN &&
                req.Options.CertificateTransparencyLoggingPreference == CertificateTransparencyLoggingPreference.ENABLED
            ));
        }

        [Test]
        public async Task Delete_WithCreationRoleArn_CallsCreateAcmFactoryWithRoleArn()
        {
            AcmFactory acmFactory;
            Route53Factory route53Factory;
            LambdaFactory lambdaFactory;

            var instance = CreateInstance(out acmFactory, out route53Factory, out lambdaFactory);
            var roleArn = "arn";
            instance.Request.ResourceProperties.CreationRoleArn = roleArn;

            await instance.Delete();
            await acmFactory.Received().Create(Arg.Is<string>(val => val == roleArn));
        }

        [Test]
        public async Task Delete_CallsDeleteCertificate()
        {
            var acmClient = CreateAcmClient();
            var route53Client = CreateRoute53Client();
            var instance = CreateInstance(acmClient: acmClient, route53Client: route53Client);

            await instance.Delete();
            await acmClient.Received().DeleteCertificateAsync(Arg.Is<DeleteCertificateRequest>(req =>
                req.CertificateArn == ARN
            ));
        }

        [Test]
        public async Task Delete_CallsChangeRecordSets()
        {
            var acmClient = CreateAcmClient();
            var route53Client = CreateRoute53Client();
            var instance = CreateInstance(acmClient: acmClient, route53Client: route53Client);

            await instance.Delete();
            await route53Client.Received().ChangeResourceRecordSetsAsync(Arg.Is<ChangeResourceRecordSetsRequest>(req =>
                req.HostedZoneId == "ABC123" &&
                req.ChangeBatch.Changes.Any(change =>
                    change.Action == ChangeAction.DELETE &&
                    change.ResourceRecordSet.Type == RRType.CNAME &&
                    change.ResourceRecordSet.Name == "_x1.example.com" &&
                    change.ResourceRecordSet.ResourceRecords.Any(record =>
                        record.Value == "example-com.acm-validations.aws"
                    )
                )
            ));
        }

        [Test]
        public async Task Delete_WithValidationRoleArn_CallsCreateRoute53FactoryWithRoleArn()
        {
            AcmFactory acmFactory;
            Route53Factory route53Factory;
            LambdaFactory lambdaFactory;

            var instance = CreateInstance(out acmFactory, out route53Factory, out lambdaFactory);
            var roleArn = "role";
            instance.Request.ResourceProperties.ValidationRoleArn = roleArn;

            await instance.Delete();
            await route53Factory.Received().Create(Arg.Is<string>(val => val == roleArn));
        }
    }
}