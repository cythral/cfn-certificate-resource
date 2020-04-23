using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

using Amazon.CertificateManager;
using Amazon.CertificateManager.Model;
using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.Route53;
using Amazon.Route53.Model;

using Cythral.CloudFormation.CustomResource.Core;
using Cythral.CloudFormation.CustomResource.Attributes;
using Cythral.CloudFormation.Resources.Factories;

using Tag = Amazon.CertificateManager.Model.Tag;
using ResourceRecord = Amazon.Route53.Model.ResourceRecord;

namespace Cythral.CloudFormation.Resources
{

    /// <summary>
    /// Certificate Custom Resource supporting automatic dns validation
    /// </summary>
    [CustomResource(ResourcePropertiesType = typeof(Certificate.Properties))]
    public partial class Certificate
    {

        public class Properties
        {
            [UpdateRequiresReplacement]
            [Required]
            public string DomainName { get; set; } = "";

            [UpdateRequiresReplacement]
            public List<DomainValidationOption>? DomainValidationOptions { get; set; }

            [UpdateRequiresReplacement]
            public List<string>? SubjectAlternativeNames { get; set; }

            public List<Tag>? Tags { get; set; }

            [UpdateRequiresReplacement]
            public ValidationMethod ValidationMethod { get; set; } = ValidationMethod.DNS;

            [UpdateRequiresReplacement]
            public string? HostedZoneId { get; set; }

            /// <summary>
            /// Role used when creating the certificate
            /// </summary>
            public string? CreationRoleArn { get; set; }

            /// <summary>
            /// Role used when validating ownership the certificate via DNS
            /// </summary>
            public string? ValidationRoleArn { get; set; }

            public CertificateOptions? Options { get; set; }

            [UpdateRequiresReplacement]
            public string? CertificateAuthorityArn { get; set; }
        }

        public static int WaitInterval { get; set; } = 30;

        private AcmFactory acmFactory = new AcmFactory();
        private Route53Factory route53Factory = new Route53Factory();
        private LambdaFactory lambdaFactory = new LambdaFactory();


        /// <summary>
        /// Tags that have been updated or inserted since creation or last update
        /// </summary>
        /// <value></value>
        public IEnumerable<Tag> UpsertedTags
        {
            get
            {
                var prev = from tag in Request.OldResourceProperties?.Tags ?? new List<Tag>()
                           select new { Key = tag.Key, Value = tag.Value };

                var curr = from tag in Request.ResourceProperties?.Tags ?? new List<Tag>()
                           select new { Key = tag.Key, Value = tag.Value };

                return from tag in curr.Except(prev)
                       select new Tag { Key = tag.Key, Value = tag.Value };
            }
        }

        /// <summary>
        /// Tags that were deleted since creation or last update
        /// </summary>
        /// <value>List of names of deleted tags</value>
        public IEnumerable<Tag> DeletedTags
        {
            get
            {
                var oldKeys = from tag in Request.OldResourceProperties?.Tags ?? new List<Tag>() select tag.Key;
                var newKeys = from tag in Request.ResourceProperties?.Tags ?? new List<Tag>() select tag.Key;

                return from key in oldKeys.Except(newKeys)
                       join tag in Request.OldResourceProperties?.Tags ?? new List<Tag>() on key equals tag.Key
                       select tag;
            }
        }

        public async Task<Response?> Create()
        {
            var props = Request.ResourceProperties;
            
            IAmazonCertificateManager acmClient = await acmFactory.Create(props.CreationRoleArn);
            IAmazonRoute53 route53Client = await route53Factory.Create(props.ValidationRoleArn);
            
            var request = new RequestCertificateRequest
            {
                DomainName = props.DomainName,
                ValidationMethod = props.ValidationMethod
            };

            if (props.CertificateAuthorityArn != null) request.CertificateAuthorityArn = props.CertificateAuthorityArn;
            if (props.DomainValidationOptions != null) request.DomainValidationOptions = props.DomainValidationOptions;
            if (props.Options != null) request.Options = props.Options;
            if (props.SubjectAlternativeNames != null) request.SubjectAlternativeNames = props.SubjectAlternativeNames;

            var requestCertificateResponse = await acmClient.RequestCertificateAsync(request);
            Console.WriteLine($"Got Request Certificate Response: {JsonSerializer.Serialize(requestCertificateResponse)}");

            PhysicalResourceId = requestCertificateResponse.CertificateArn;
            var describeCertificateRequest = new DescribeCertificateRequest { CertificateArn = PhysicalResourceId };
            var tasks = new List<Task>();

            Thread.Sleep(500);
            bool foundValidationOptions = false;
            List<DomainValidation> validationOptions = new List<DomainValidation>();

            // For some reason, the domain validation options aren't immediately populated.
            while (!foundValidationOptions)
            {
                var describeCertificateResponse = await acmClient.DescribeCertificateAsync(describeCertificateRequest);
                Console.WriteLine($"Got Describe Certificate Response: {JsonSerializer.Serialize(describeCertificateResponse)}");

                validationOptions = describeCertificateResponse.Certificate.DomainValidationOptions;
                foundValidationOptions = true;

                if (validationOptions.Count() == 0) foundValidationOptions = false;

                foreach (var option in validationOptions)
                {
                    if (option.ResourceRecord?.Name == null) foundValidationOptions = false;
                }

                Thread.Sleep(1000);
            }

            if (props.Tags != null)
            {
                tasks.Add(Task.Run(async delegate
                {
                    var addTagsResponse = await acmClient.AddTagsToCertificateAsync(new AddTagsToCertificateRequest
                    {
                        Tags = props.Tags,
                        CertificateArn = PhysicalResourceId,
                    });

                    Console.WriteLine($"Got Add Tags Response: {JsonSerializer.Serialize(addTagsResponse)}");
                }));
            }

            // add DNS validation records if applicable
            var names = new HashSet<string>();
            var changes = new List<Change>();

            if (props.ValidationMethod == ValidationMethod.DNS)
            {
                foreach (var option in validationOptions)
                {
                    var query = from name in names where name == option.ResourceRecord.Name select name;

                    if (query.Count() != 0)
                    {
                        continue;
                    }

                    names.Add(option.ResourceRecord.Name);
                    changes.Add(new Change
                    {
                        Action = ChangeAction.UPSERT,
                        ResourceRecordSet = new ResourceRecordSet
                        {
                            Name = option.ResourceRecord.Name,
                            Type = new RRType(option.ResourceRecord.Type.Value),
                            SetIdentifier = PhysicalResourceId,
                            Weight = 1,
                            TTL = 60,
                            ResourceRecords = new List<ResourceRecord> {
                                new ResourceRecord { Value = option.ResourceRecord.Value }
                            }
                        }
                    });
                }

                tasks.Add(
                    Task.Run(async delegate
                    {
                        var changeRecordsResponse = await route53Client.ChangeResourceRecordSetsAsync(new ChangeResourceRecordSetsRequest
                        {
                            HostedZoneId = props.HostedZoneId,
                            ChangeBatch = new ChangeBatch
                            {
                                Changes = changes
                            }
                        });

                        Console.WriteLine($"Got Change Record Sets Response: {JsonSerializer.Serialize(changeRecordsResponse)}");
                    })
                );
            }

            Task.WaitAll(tasks.ToArray());

            Request.PhysicalResourceId = PhysicalResourceId;
            Request.RequestType = RequestType.Wait;

            return await Wait();
        }

        public async Task<Response?> Wait()
        {
            var props = Request.ResourceProperties;
            IAmazonCertificateManager acmClient = await acmFactory.Create(props.CreationRoleArn);
            IAmazonLambda lambdaClient = await lambdaFactory.Create();
           
            var request = new DescribeCertificateRequest { CertificateArn = PhysicalResourceId };
            var response = await acmClient.DescribeCertificateAsync(request);
            var status = response?.Certificate?.Status?.Value;

            switch (status)
            {
                case "PENDING_VALIDATION":
                    Thread.Sleep(WaitInterval * 1000);
                    
                    var invokeResponse = await lambdaClient.InvokeAsync(new InvokeRequest
                    {
                        FunctionName = Context.FunctionName,
                        Payload = JsonSerializer.Serialize(Request, SerializerOptions),
                        InvocationType = InvocationType.Event,
                    });

                    break;

                case "ISSUED": return new Response();
                default: throw new Exception($"Certificate could not be issued. (Got status: {status})");
            }

            return null;
        }


        public async Task<Response> Update()
        {
            var oldProps = Request.OldResourceProperties;
            var newProps = Request.ResourceProperties;
            IAmazonCertificateManager acmClient = await acmFactory.Create(newProps.CreationRoleArn);

            Task.WaitAll(new Task[] {

                // add new tags
                Task.Run(async delegate {
                    var upsertTagsResponse = await acmClient.AddTagsToCertificateAsync(new AddTagsToCertificateRequest {
                        Tags = UpsertedTags.ToList(),
                        CertificateArn = Request.PhysicalResourceId
                    });

                    Console.WriteLine($"Received upsert tags response: {JsonSerializer.Serialize(upsertTagsResponse)}");
                }),

                // delete old tags
                Task.Run(async delegate {
                    var deleteTagsResponse = await acmClient.RemoveTagsFromCertificateAsync(new RemoveTagsFromCertificateRequest {
                        Tags = DeletedTags.ToList(),
                        CertificateArn = Request.PhysicalResourceId
                    });

                    Console.WriteLine($"Received delete tags response: {JsonSerializer.Serialize(deleteTagsResponse)}");
                }),

                // update options
                Task.Run(async delegate {
                    if(newProps?.Options?.CertificateTransparencyLoggingPreference != oldProps?.Options?.CertificateTransparencyLoggingPreference) {
                        var updateOptionsResponse = await acmClient.UpdateCertificateOptionsAsync(new UpdateCertificateOptionsRequest {
                            CertificateArn = Request.PhysicalResourceId,
                            Options = newProps?.Options
                        });

                        Console.WriteLine($"Received update options response: {JsonSerializer.Serialize(updateOptionsResponse)}");
                    }
                })
            });

            return new Response
            {
                PhysicalResourceId = Request.PhysicalResourceId
            };
        }

        public async Task<Response> Delete()
        {
            var props = Request.ResourceProperties;
            IAmazonCertificateManager acmClient = await acmFactory.Create(props.CreationRoleArn);
            IAmazonRoute53 route53Client = await route53Factory.Create(props.ValidationRoleArn);
            
            var describeResponse = await acmClient.DescribeCertificateAsync(new DescribeCertificateRequest
            {
                CertificateArn = Request.PhysicalResourceId,
            });
            Console.WriteLine($"Got describe certificate response: {JsonSerializer.Serialize(describeResponse)}");

            var names = new HashSet<string>();
            var changes = new List<Change>();

            foreach (var option in describeResponse.Certificate.DomainValidationOptions)
            {
                var query = from name in names where name == option.ResourceRecord.Name select name;

                if (query.Count() != 0)
                {
                    continue;
                }

                names.Add(option.ResourceRecord.Name);
                changes.Add(new Change
                {
                    Action = ChangeAction.DELETE,
                    ResourceRecordSet = new ResourceRecordSet
                    {
                        Name = option.ResourceRecord.Name,
                        Type = new RRType(option.ResourceRecord.Type.Value),
                        SetIdentifier = Request.PhysicalResourceId,
                        Weight = 1,
                        TTL = 60,
                        ResourceRecords = new List<ResourceRecord> {
                            new ResourceRecord { Value = option.ResourceRecord.Value }
                        }
                    }
                });
            }

            if (changes.Count() != 0)
            {
                try
                {
                    var roleArn = Request.ResourceProperties.ValidationRoleArn;
                    var changeRecordsResponse = await route53Client.ChangeResourceRecordSetsAsync(new ChangeResourceRecordSetsRequest
                    {
                        HostedZoneId = Request.ResourceProperties.HostedZoneId,
                        ChangeBatch = new ChangeBatch
                        {
                            Changes = changes
                        }
                    });

                    Console.WriteLine($"Got delete record response: {JsonSerializer.Serialize(changeRecordsResponse)}");
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Error deleting old resource records: {e.Message} {e.StackTrace}");
                }
            }

            var deleteResponse = await acmClient.DeleteCertificateAsync(new DeleteCertificateRequest
            {
                CertificateArn = Request.PhysicalResourceId,
            });

            Console.WriteLine($"Received delete certificate response: {JsonSerializer.Serialize(deleteResponse)}");

            return new Response
            {
                PhysicalResourceId = Request.PhysicalResourceId
            };
        }
    }
}