using FluentAssertions;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.Tasks.Domain.Tasks;
using S = NexUS.Tasks.Domain.Tasks.TaskItemStatus;

namespace NexUS.Tasks.UnitTests;

public class TaskStatusTests
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Transisi yang diizinkan menurut story NX-033; semua pasangan lain harus ditolak.</summary>
    private static readonly HashSet<(S From, S To)> Allowed =
    [
        (S.Todo, S.InProgress), (S.InProgress, S.InReview), (S.InReview, S.Done),
        (S.Todo, S.Blocked), (S.InProgress, S.Blocked), (S.InReview, S.Blocked),
        (S.Blocked, S.Todo), (S.Blocked, S.InProgress), (S.Blocked, S.InReview),
        (S.Todo, S.Cancelled), (S.InProgress, S.Cancelled), (S.InReview, S.Cancelled), (S.Blocked, S.Cancelled),
        (S.Done, S.InProgress),
    ];

    public static TheoryData<S, S> AllPairs()
    {
        var data = new TheoryData<S, S>();
        foreach (var from in Enum.GetValues<S>())
        foreach (var to in Enum.GetValues<S>())
            if (from != to) data.Add(from, to);
        return data;
    }

    private static TaskItem NewTask() =>
        TaskItem.Create(7, Guid.NewGuid(), null, "Tugas", null, TaskPriority.Medium, null, Guid.NewGuid());

    /// <summary>Membawa tugas ke <paramref name="target"/> lewat jalur yang sah.</summary>
    private static TaskItem TaskIn(S target)
    {
        var task = NewTask();
        S[] path = target switch
        {
            S.Todo => [],
            S.InProgress => [S.InProgress],
            S.InReview => [S.InProgress, S.InReview],
            S.Done => [S.InProgress, S.InReview, S.Done],
            S.Blocked => [S.Blocked],
            S.Cancelled => [S.Cancelled],
            _ => throw new ArgumentOutOfRangeException(nameof(target)),
        };
        foreach (var step in path) task.ChangeStatus(step, Actor, Now);
        task.PopDomainEvents();
        return task;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void Setiap_pasangan_status_diizinkan_atau_ditolak_sesuai_aturan(S from, S to)
    {
        var task = TaskIn(from);
        var historyBefore = task.StatusHistory.Count;

        var act = () => task.ChangeStatus(to, Actor, Now);

        TaskStatusRules.CanTransition(from, to).Should().Be(Allowed.Contains((from, to)));
        if (Allowed.Contains((from, to)))
        {
            act.Should().NotThrow().Which.Should().BeTrue();
            task.Status.Should().Be(to);
            task.StatusHistory.Should().HaveCount(historyBefore + 1);
            task.StatusHistory.Last().Should().Match<TaskStatusChange>(h =>
                h.FromStatus == from && h.ToStatus == to && h.ChangedBy == Actor && h.ChangedAt == Now);
            task.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<TaskStatusChangedDomainEvent>().Which.Should().BeEquivalentTo(new
            {
                TenantId = 7L, TaskId = task.PublicId, task.ProjectId, FromStatus = TaskItem.ToName(from), ToStatus = TaskItem.ToName(to),
                ChangedBy = Actor, ChangedAt = Now,
            });
        }
        else
        {
            act.Should().Throw<DomainException>().WithMessage(
                $"Status tugas tidak bisa diubah dari {TaskItem.ToName(from).ToLowerInvariant()} ke {TaskItem.ToName(to).ToLowerInvariant()}.*");
            task.Status.Should().Be(from);
            task.StatusHistory.Should().HaveCount(historyBefore);
            task.DomainEvents.Should().BeEmpty();
        }
    }

    [Fact]
    public void Pesan_transisi_tidak_valid_menyebut_status_berikutnya_yang_diizinkan()
    {
        var act = () => TaskIn(S.Todo).ChangeStatus(S.Done, Actor, Now);
        act.Should().Throw<DomainException>()
            .WithMessage("Status tugas tidak bisa diubah dari todo ke done. Dari todo hanya bisa ke: in_progress, blocked, cancelled.");

        var final = () => TaskIn(S.Cancelled).ChangeStatus(S.Todo, Actor, Now);
        final.Should().Throw<DomainException>().WithMessage("*Tugas berstatus cancelled sudah final.");
    }

    [Fact]
    public void Selesai_mengisi_CompletedAt_dan_buka_ulang_mengosongkannya()
    {
        var task = TaskIn(S.InReview);
        task.CompletedAt.Should().BeNull();

        task.ChangeStatus(S.Done, Actor, Now);
        task.CompletedAt.Should().Be(Now);

        task.ChangeStatus(S.InProgress, Actor, Now.AddHours(1));
        task.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Status_yang_sama_tidak_berbuat_apa_apa()
    {
        var task = TaskIn(S.InProgress);

        task.ChangeStatus(S.InProgress, Actor, Now).Should().BeFalse();
        task.StatusHistory.Should().HaveCount(1, "hanya transisi todo → in_progress");
        task.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Tugas_terhapus_atau_status_tidak_dikenal_ditolak()
    {
        var deleted = NewTask();
        deleted.Delete();
        ((Action)(() => deleted.ChangeStatus(S.InProgress, Actor, Now))).Should().Throw<DomainException>();

        ((Action)(() => NewTask().ChangeStatus((S)99, Actor, Now))).Should().Throw<DomainException>();
        ((Action)(() => NewTask().ChangeStatus(S.InProgress, Guid.Empty, Now))).Should().Throw<DomainException>();
    }

    [Fact]
    public void Status_tanpa_transisi_keluar_hanya_cancelled()
    {
        Enum.GetValues<S>().Where(s => TaskStatusRules.AllowedFrom(s).Count == 0).Should().Equal(S.Cancelled);
    }
}
