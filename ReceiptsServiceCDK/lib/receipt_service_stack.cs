using Amazon.CDK;
using Amazon.CDK.AWS.APIGateway;
using Amazon.CDK.AWS.Lambda;
using Amazon.CDK.AWS.S3;
using Amazon.CDK.AWS.DynamoDB;
using Amazon.CDK.AWS.SQS;
using Amazon.CDK.AWS.IAM;
using Amazon.CDK.AWS.CloudWatch;
using System.Collections.Generic;

namespace ReceiptServiceCDK
{
    public class ReceiptServiceStack : Stack
    {
        internal ReceiptServiceStack(Construct scope, string id, IStackProps props = null) : base(scope, id, props)
        {
            // S3 Bucket for receipt archives
            var receiptArchiveStorage = new Bucket(this, "ReceiptArchiveStorage", new BucketProps
            {
                LifecycleRules = new[] {
                    new LifecycleRule {
                        Transitions = new[] {
                            new Transition {
                                StorageClass = StorageClass.GLACIER,
                                TransitionAfter = Duration.Days(30)
                            }
                        }
                    }
                }
            });

            // DynamoDB table for recent receipts with Streams enabled
            var receiptTable = new Table(this, "ReceiptTable", new TableProps
            {
                BillingMode = BillingMode.PAY_PER_REQUEST,
                PartitionKey = new Attribute
                {
                    Name = "ReceiptId",
                    Type = AttributeType.STRING
                },
                Stream = StreamViewType.NEW_IMAGE
            });

            // IAM roles for Lambda functions
            var receiptHandlerLambdaRole = new Role(this, "ReceiptHandlerLambdaRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
                Description = "IAM role for the Receipt Handler Lambda",
                ManagedPolicies = new[] {
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
                }
            });

            

            var getReceiptStatusLambdaRole = new Role(this, "GetReceiptStatusLambdaRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("lambda.amazonaws.com"),
                Description = "IAM role for the Get Receipt Status Lambda",
                ManagedPolicies = new[] {
                    ManagedPolicy.FromAwsManagedPolicyName("service-role/AWSLambdaBasicExecutionRole")
                }
            });

            // Granting permissions to Lambda roles
            receiptHandlerLambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { "dynamodb:GetItem", "dynamodb:PutItem", "dynamodb:UpdateItem", "dynamodb:Query", "dynamodb:Scan" },
                Resources = new[] { receiptTable.TableArn }
            }));
            receiptHandlerLambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { "sqs:SendMessage" },
                Resources = new[] { /* Add SQS Queue ARNs here */ }
            }));
            receiptHandlerLambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { "s3:PutObject" },
                Resources = new[] { receiptArchiveStorage.BucketArn + "/*" }
            }));

            getReceiptStatusLambdaRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { "dynamodb:GetItem", "dynamodb:Query", "dynamodb:Scan" },
                Resources = new[] { receiptTable.TableArn }
            }));

            // Lambda function for receipt handling
            var receiptHandlerLambda = new Function(this, "ReceiptHandler", new FunctionProps
            {
                Runtime = Runtime.DOTNET_CORE_3_1,
                Code = Code.FromAsset("path/to/receipt/handler/project"),
                Handler = "ReceiptHandlerNamespace.ReceiptHandler::FunctionHandler",
                Environment = new Dictionary<string, string> {
                    { "RECEIPT_TABLE_NAME", receiptTable.TableName },
                    // Add other environment variables if needed
                },
                Role = receiptHandlerLambdaRole,
                MemorySize = 512,
                Timeout = Duration.Seconds(30)
            });


            // Lambda function for getting receipt status
            var getReceiptStatusLambda = new Function(this, "GetReceiptStatus", new FunctionProps
            {
                Runtime = Runtime.DOTNET_CORE_3_1,
                Code = Code.FromAsset("path/to/status/handler/project"),
                Handler = "StatusHandlerNamespace.StatusHandler::FunctionHandler",
                Environment = new Dictionary<string, string> {
                    { "RECEIPT_TABLE_NAME", receiptTable.TableName }
                },
                Role = getReceiptStatusLambdaRole,
                MemorySize = 256,
                Timeout = Duration.Seconds(10)
            });

            
            // Warm-up schedule: Active during day hours, paused or minimal during night hours
            var warmUpSchedule = new CompositeSchedule(
                Schedule.Expression("cron(0/10 6-23 ? *)"), // Every 10 minutes during the day (6 AM to 11 PM) on weekdays
            );

            // Schedule to keep the Lambda function warm
            new Rule(this, "KeepLambdaWarmRule", new RuleProps
            {
                Schedule = warmUpSchedule,
                Targets = new IRuleTarget[]
                {
                    new LambdaFunction(receiptHandlerLambda)
                    new LambdaFunction(getReceiptStatusLambda)
                }
            });

            // API Gateway setup
            var api = new RestApi(this, "ReceiptAPI", new RestApiProps
            {
                RestApiName = "Receipt Service API",
                Description = "API for managing receipts",
                DefaultCorsPreflightOptions = new CorsOptions
                {
                    AllowOrigins = Cors.ALL_ORIGINS,
                    AllowMethods = Cors.ALL_METHODS
                },
                DeployOptions = new StageOptions
                {
                    ThrottlingRateLimit = 1000,
                    ThrottlingBurstLimit = 2000
                }
            });

            // API Resources and Methods
            var receiptsResource = api.Root.AddResource("receipts");
            receiptsResource.AddMethod("POST", new LambdaIntegration(receiptHandlerLambda));
            var receiptResource = receiptsResource.AddResource("{id}");
            receiptResource.AddMethod("GET", new LambdaIntegration(getReceiptStatusLambda));

            // CloudWatch for logging and monitoring
            var logGroup = new LogGroup(this, "ReceiptServiceLogGroup", new LogGroupProps
            {
                LogGroupName = "/aws/lambda/ReceiptService",
                Retention = RetentionDays.ONE_MONTH
            });

            var athenaQueryRole = new Role(this, "AthenaQueryRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("athena.amazonaws.com"),
                ManagedPolicies = new[] {
                    ManagedPolicy.FromAwsManagedPolicyName("AmazonAthenaFullAccess")
                }
            });

            athenaQueryRole.AddToPolicy(new PolicyStatement(new PolicyStatementProps
            {
                Actions = new[] { "s3:*" },
                Resources = new[] { receiptArchiveStorage.BucketArn + "/*", athenaResultsBucket.BucketArn + "/*" }
            }));


            var sourceOutput = new Artifact_();
            var buildOutput = new Artifact_();

            // Source action (e.g., CodeCommit, GitHub, Bitbucket)
            var sourceAction = new CodeCommitSourceAction(new CodeCommitSourceActionProps
            {
                ActionName = "CodeCommit",
                Repository = /* CodeCommit repository */,
                Output = sourceOutput
            });

            // CodeBuild project
            var buildProject = new PipelineProject(this, "BuildProject", new PipelineProjectProps
            {
                Environment = new BuildEnvironment
                {
                    BuildImage = LinuxBuildImage.STANDARD_5_0
                },
            });

            // Build action
            var buildAction = new CodeBuildAction(new CodeBuildActionProps
            {
                ActionName = "Build",
                Project = buildProject,
                Input = sourceOutput,
                Outputs = new[] { buildOutput }
            });

            // CodePipeline
            var pipeline = new Pipeline(this, "Pipeline", new PipelineProps
            {
                PipelineName = "ReceiptServicePipeline",
                Stages = new[]
                {
                    new StageProps
                    {
                        StageName = "Source",
                        Actions = new[] { sourceAction }
                    },
                    new StageProps
                    {
                        StageName = "Build",
                        Actions = new[] { buildAction }
                    },
                    // Add additional stages like 'Test', 'Deploy' as needed
                }
            });

            // IAM role for the pipeline
            var pipelineRole = new Role(this, "PipelineRole", new RoleProps
            {
                AssumedBy = new ServicePrincipal("codepipeline.amazonaws.com"),
                // Add policies allowing the pipeline to access necessary resources
            });

            pipeline.Role = pipelineRole;

        }
    }
}
