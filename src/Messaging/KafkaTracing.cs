using System.Diagnostics;
using System.Text;
using Confluent.Kafka;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;

namespace Portal.Messaging;

/// <summary>
/// Trägt den Trace-Kontext über Kafka: Der Producer schreibt <c>traceparent</c> in die Header,
/// der Consumer setzt den Trace damit fort. So wird aus Simulator → Kafka → Quote ein einziger Trace.
/// </summary>
public static class KafkaTracing
{
    public const string SourceName = "Portal.Messaging";

    private static readonly ActivitySource Source = new(SourceName);
    // Explizit W3C statt Propagators.DefaultTextMapPropagator: Der Default ist ein No-op, bis das OTel-SDK startet.
    private static readonly TextMapPropagator Propagator =
        new CompositeTextMapPropagator([new TraceContextPropagator(), new BaggagePropagator()]);

    public static Activity? StartPublish(string topic, Headers headers)
    {
        var activity = Source.StartActivity($"{topic} publish", ActivityKind.Producer);
        Tag(activity, topic);

        var context = (activity ?? Activity.Current)?.Context;
        if (context is { } ctx)
        {
            Propagator.Inject(new PropagationContext(ctx, Baggage.Current), headers,
                static (h, key, value) => { h.Remove(key); h.Add(key, Encoding.UTF8.GetBytes(value)); });
        }

        return activity;
    }

    public static Activity? StartConsume(string topic, Headers headers)
    {
        var parent = Propagator.Extract(default, headers, static (h, key) =>
            h.TryGetLastBytes(key, out var bytes) ? [Encoding.UTF8.GetString(bytes)] : []);

        var activity = Source.StartActivity($"{topic} process", ActivityKind.Consumer, parent.ActivityContext);
        Tag(activity, topic);
        return activity;
    }

    private static void Tag(Activity? activity, string topic)
    {
        activity?.SetTag("messaging.system", "kafka");
        activity?.SetTag("messaging.destination.name", topic);
    }
}
