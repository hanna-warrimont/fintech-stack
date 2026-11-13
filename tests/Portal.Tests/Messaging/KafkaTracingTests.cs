using System.Diagnostics;
using Confluent.Kafka;
using Portal.Messaging;

namespace Portal.Tests.Messaging;

public sealed class KafkaTracingTests : IDisposable
{
    private readonly ActivityListener _listener = new()
    {
        ShouldListenTo = s => s.Name == KafkaTracing.SourceName,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    };

    public KafkaTracingTests() => ActivitySource.AddActivityListener(_listener);

    public void Dispose() => _listener.Dispose();

    [Fact]
    public void Consume_continues_trace_of_publish()
    {
        var headers = new Headers();
        using var publish = KafkaTracing.StartPublish(Topics.Ticks, headers);
        publish!.Stop();
        using var consume = KafkaTracing.StartConsume(Topics.Ticks, headers);

        Assert.Equal(ActivityKind.Producer, publish.Kind);
        Assert.Equal(ActivityKind.Consumer, consume!.Kind);
        Assert.Equal(publish.TraceId, consume.TraceId);
        Assert.Equal(publish.SpanId, consume.ParentSpanId);
        Assert.NotNull(headers.FirstOrDefault(h => h.Key == "traceparent"));
    }

    [Fact]
    public void Publish_with_explicit_parent_continues_that_trace()
    {
        var parent = new ActivityContext(ActivityTraceId.CreateRandom(), ActivitySpanId.CreateRandom(), ActivityTraceFlags.Recorded);

        using var publish = KafkaTracing.StartPublish(Topics.Ticks, new Headers(), parent);

        Assert.Equal(parent.TraceId, publish!.TraceId);
        Assert.Equal(parent.SpanId, publish.ParentSpanId);
    }

    [Fact]
    public void Consume_without_header_starts_new_trace()
    {
        using var consume = KafkaTracing.StartConsume(Topics.Ticks, new Headers());

        Assert.NotNull(consume);
        Assert.Equal(default, consume.ParentSpanId);
    }
}
