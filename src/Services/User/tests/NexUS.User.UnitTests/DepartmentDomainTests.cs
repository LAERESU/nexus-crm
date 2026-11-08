using FluentAssertions;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Domain.Departments;

namespace NexUS.User.UnitTests;

public class DepartmentDomainTests
{
    [Fact]
    public void Create_menormalkan_kode_ke_huruf_besar()
    {
        var department = Department.Create(1, " hr-01 ", "Sumber Daya Manusia", parentId: null);

        department.Code.Should().Be("HR-01");
        department.Name.Should().Be("Sumber Daya Manusia");
    }

    [Fact]
    public void MoveTo_menolak_induk_yang_merupakan_turunan_sendiri()
    {
        var department = Department.Create(1, "A", "A", null);

        // Calon induk punya leluhur yang memuat departemen ini (ID 0 pada objek baru).
        FluentActions.Invoking(() => department.MoveTo(newParentId: 5, ancestorIds: [5, department.Id]))
            .Should().Throw<DomainException>();
    }

    [Fact]
    public void MoveTo_menerima_induk_tanpa_siklus()
    {
        var department = Department.Create(1, "A", "A", null);

        department.MoveTo(newParentId: 5, ancestorIds: [5, 2]);

        department.ParentId.Should().Be(5);
    }

    [Fact]
    public void MoveTo_ke_akar_menghapus_induk()
    {
        var department = Department.Create(1, "A", "A", parentId: 5);

        department.MoveTo(null, []);

        department.ParentId.Should().BeNull();
    }
}
