using System;
using NUnit.Framework;

namespace Cythral.CloudFormation.Resources.Tests
{
    public class TestSuite
    {
        [SetUp]
        void SetUpEnvironmentVariable()
        {
            Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
        }
    }
}