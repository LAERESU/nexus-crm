using FluentAssertions;
using NexUS.BuildingBlocks.Common.Domain;
using NexUS.BuildingBlocks.Common.Exceptions;
using NexUS.User.Domain.Roles;
using NexUS.User.Domain.Users;
using UserEntity = NexUS.User.Domain.Users.User;

namespace NexUS.User.UnitTests;

public class DefaultRoleCatalogTests
{
    private static readonly string[] AllCodes = DefaultRoleCatalog.Permissions.Select(p => p.Code).ToArray();

    [Fact]
    public void Kode_permission_unik_dan_berformat_modul_aksi()
    {
        AllCodes.Should().OnlyHaveUniqueItems();
        foreach (var definition in DefaultRoleCatalog.Permissions)
            FluentActions.Invoking(() => Permission.Create(definition.Code, definition.Description)).Should().NotThrow();
    }

    [Fact]
    public void Setiap_permission_di_role_bawaan_ada_di_katalog()
    {
        // Seeder mencari permission berdasarkan kode; kode yang salah ketik akan membuat startup gagal.
        foreach (var role in DefaultRoleCatalog.Roles)
            role.Permissions.Should().BeSubsetOf(AllCodes, $"role {role.Code}").And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void Kode_role_bawaan_unik_dan_valid()
    {
        DefaultRoleCatalog.Roles.Select(r => r.Code).Should().OnlyHaveUniqueItems().And.OnlyContain(c => Role.NormalizeCode(c) == c);
    }

    [Fact]
    public void Tenant_admin_dapat_mengelola_user_dan_role()
    {
        DefaultRoleCatalog.Roles.Single(r => r.Code == "TENANT_ADMIN").Permissions
            .Should().Contain(["user.manage", "role.manage", "sop.category.manage"]);
    }

    [Fact]
    public void Auditor_hanya_memiliki_permission_baca()
    {
        DefaultRoleCatalog.Roles.Single(r => r.Code == "AUDITOR").Permissions.Should().OnlyContain(p => p.EndsWith(".read"));
    }

    [Fact]
    public void Penulis_sop_tidak_dapat_menyetujui_atau_menerbitkan()
    {
        // Segregation of duties (CONTEXT.md bagian 8): penulis tidak boleh menyetujui SOP-nya sendiri.
        DefaultRoleCatalog.Roles.Single(r => r.Code == "SOP_AUTHOR").Permissions
            .Should().NotContain(["sop.approve", "sop.publish", "sop.review"]);
    }
}

public class RoleDomainTests
{
    private static Permission Perm(long id, string code)
    {
        var permission = Permission.Create(code, null);
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(permission, id); // ID internal biasanya diisi database
        return permission;
    }

    private static Role CustomRole(params Permission[] permissions) => Role.Create(1, "kustom", "Kustom", null, permissions);

    [Fact]
    public void Create_menormalkan_kode_dan_menghapus_permission_ganda()
    {
        var role = Role.Create(1, " peran_baru ", "Peran Baru", " deskripsi ", [Perm(1, "task.read"), Perm(1, "task.read")]);

        role.Code.Should().Be("PERAN_BARU");
        role.Description.Should().Be("deskripsi");
        role.Permissions.Should().ContainSingle();
        role.IsSystem.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("1ABC")]
    [InlineData("ada spasi")]
    [InlineData("ada-strip")]
    public void Create_menolak_kode_yang_tidak_valid(string code)
    {
        FluentActions.Invoking(() => Role.Create(1, code, "Nama", null, [])).Should().Throw<DomainException>();
    }

    [Fact]
    public void Role_sistem_tidak_dapat_diubah_dihapus_atau_diganti_permission_nya()
    {
        var system = Role.Create(1, "TENANT_ADMIN", "Admin", null, [], isSystem: true);

        FluentActions.Invoking(() => system.Update("Baru", null)).Should().Throw<DomainException>();
        FluentActions.Invoking(() => system.SetPermissions([Perm(1, "task.read")])).Should().Throw<DomainException>();
        FluentActions.Invoking(system.Delete).Should().Throw<DomainException>();
        system.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Role_sistem_dapat_disinkronkan_dari_katalog()
    {
        var system = Role.Create(1, "MEMBER", "Anggota", null, [Perm(1, "task.read")], isSystem: true);

        system.SyncSystemPermissions([Perm(1, "task.read"), Perm(2, "task.create")]);

        system.Permissions.Select(p => p.PermissionId).Should().BeEquivalentTo([1L, 2L]);
    }

    [Fact]
    public void Role_kustom_tidak_dapat_memakai_sinkronisasi_katalog()
    {
        FluentActions.Invoking(() => CustomRole().SyncSystemPermissions([])).Should().Throw<DomainException>();
    }

    [Fact]
    public void SetPermissions_mencatat_RoleChanged_hanya_bila_ada_perubahan()
    {
        var role = CustomRole(Perm(1, "task.read"));
        role.PopDomainEvents();

        role.SetPermissions([Perm(1, "task.read")]); // sama
        role.PopDomainEvents().Should().BeEmpty();

        role.SetPermissions([Perm(1, "task.read"), Perm(2, "task.create")]);
        role.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<RoleChangedDomainEvent>()
            .Which.Should().Match<RoleChangedDomainEvent>(e =>
                e.RoleId == role.PublicId && e.UserId == null && e.TenantId == 1 && e.Reason == RoleChangeReason.RolePermissionsChanged);
    }

    [Fact]
    public void SetPermissions_mencabut_permission_yang_tidak_lagi_disebut()
    {
        var role = CustomRole(Perm(1, "task.read"), Perm(2, "task.create"));

        role.SetPermissions([Perm(2, "task.create")]);

        role.Permissions.Select(p => p.PermissionId).Should().Equal(2L);
    }

    [Fact]
    public void Delete_pada_role_kustom_melakukan_soft_delete()
    {
        var role = CustomRole();

        role.Delete();

        role.IsDeleted.Should().BeTrue();
    }

    [Theory]
    [InlineData("task")]
    [InlineData(".read")]
    [InlineData("task.")]
    public void Permission_menolak_kode_yang_bukan_modul_aksi(string code)
    {
        FluentActions.Invoking(() => Permission.Create(code, null)).Should().Throw<DomainException>();
    }
}

public class UserRolesDomainTests
{
    private static UserEntity NewUser() => UserEntity.Create(1, "budi@contoh.com", "Budi", null, null);

    [Fact]
    public void Penugasan_global_tidak_boleh_punya_scopeId()
    {
        FluentActions.Invoking(() => NewUser().SetRoles([new RoleAssignment(1, RoleScopeType.Global, Guid.NewGuid())]))
            .Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(RoleScopeType.Department)]
    [InlineData(RoleScopeType.Project)]
    public void Penugasan_dengan_scope_wajib_punya_scopeId(RoleScopeType scope)
    {
        FluentActions.Invoking(() => NewUser().SetRoles([new RoleAssignment(1, scope, null)])).Should().Throw<DomainException>();
        FluentActions.Invoking(() => NewUser().SetRoles([new RoleAssignment(1, scope, Guid.Empty)])).Should().Throw<DomainException>();
    }

    [Fact]
    public void SetRoles_mencatat_RoleChanged_untuk_user_dan_mengabaikan_duplikat()
    {
        var user = NewUser();
        user.PopDomainEvents();
        var assignment = new RoleAssignment(1, RoleScopeType.Global, null);

        user.SetRoles([assignment, assignment]);

        user.Roles.Should().ContainSingle();
        user.PopDomainEvents().Should().ContainSingle().Which.Should().BeOfType<RoleChangedDomainEvent>()
            .Which.Should().Match<RoleChangedDomainEvent>(e =>
                e.UserId == user.PublicId && e.RoleId == null && e.Reason == RoleChangeReason.UserRolesChanged);
    }

    [Fact]
    public void SetRoles_dengan_himpunan_sama_tidak_mencatat_event()
    {
        var user = NewUser();
        var department = Guid.NewGuid();
        user.SetRoles([new RoleAssignment(1, RoleScopeType.Global, null), new RoleAssignment(2, RoleScopeType.Department, department)]);
        user.PopDomainEvents();

        user.SetRoles([new RoleAssignment(2, RoleScopeType.Department, department), new RoleAssignment(1, RoleScopeType.Global, null)]);

        user.PopDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public void SetRoles_mencabut_penugasan_yang_tidak_disebut_dan_membedakan_scope()
    {
        var user = NewUser();
        var departmentA = Guid.NewGuid();
        user.SetRoles([new RoleAssignment(1, RoleScopeType.Global, null), new RoleAssignment(1, RoleScopeType.Department, departmentA)]);
        user.Roles.Should().HaveCount(2); // role yang sama pada scope berbeda adalah dua penugasan

        user.SetRoles([new RoleAssignment(1, RoleScopeType.Department, departmentA)]);

        user.Roles.Should().ContainSingle().Which.ScopeType.Should().Be(RoleScopeType.Department);
    }

    [Fact]
    public void SetRoles_menolak_lebih_dari_batas_penugasan()
    {
        var tooMany = Enumerable.Range(1, UserEntity.MaxRoleAssignments + 1)
            .Select(i => new RoleAssignment(i, RoleScopeType.Global, null)).ToList();

        FluentActions.Invoking(() => NewUser().SetRoles(tooMany)).Should().Throw<DomainException>();
    }
}
