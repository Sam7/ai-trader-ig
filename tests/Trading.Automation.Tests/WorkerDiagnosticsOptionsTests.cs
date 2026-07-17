using FluentAssertions;
using Trading.Automation.Configuration;

public sealed class WorkerDiagnosticsOptionsTests
{
    [Fact]
    public void Validate_should_accept_the_default_bounded_configuration()
    {
        var options = new WorkerDiagnosticsOptions();

        var action = options.Validate;

        action.Should().NotThrow();
    }

    [Fact]
    public void Validate_should_reject_retention_smaller_than_one_segment()
    {
        var options = new WorkerDiagnosticsOptions
        {
            SegmentMaximumBytes = 100,
            RetentionMaximumBytes = 99,
        };

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*retention*segment*");
    }

    [Fact]
    public void Validate_should_require_room_for_an_active_and_a_closed_segment()
    {
        var options = new WorkerDiagnosticsOptions
        {
            SegmentMaximumBytes = 100,
            RetentionMaximumBytes = 100,
        };

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*active*closed*segment*");
    }

    [Fact]
    public void Validate_should_reject_an_enabled_containment_policy_without_a_threshold()
    {
        var options = new WorkerDiagnosticsOptions
        {
            Containment = new WorkerDiagnosticsContainmentOptions
            {
                Enabled = true,
                ExitCgroupBytes = 0,
            },
        };

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*containment threshold*");
    }

    [Fact]
    public void Validate_should_reject_a_gcs_prefix_that_contains_no_object_path()
    {
        var options = new WorkerDiagnosticsOptions
        {
            UploadClosedSegments = true,
            GcsPrefix = "/",
        };

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*GCS prefix*");
    }

    [Fact]
    public void Validate_should_require_a_positive_artifact_upload_timeout()
    {
        var options = new WorkerDiagnosticsOptions
        {
            ArtifactUploadTimeout = TimeSpan.Zero,
        };

        var action = options.Validate;

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*upload timeout*");
    }
}
