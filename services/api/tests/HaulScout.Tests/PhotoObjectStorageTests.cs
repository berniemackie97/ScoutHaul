using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using HaulScout.Application.Inventory;
using HaulScout.Domain.Entities;
using HaulScout.Infrastructure.Inventory;

namespace HaulScout.Tests;

/// <summary>
/// Photo images kept in a bucket instead of the database. What matters here is that moving them changes nothing an
/// operator or a shopper can see: a photo serves the same bytes whichever store holds it, a photo stored before the
/// bucket existed keeps working, and a bucket that loses an object is loud rather than silent.
/// </summary>
public sealed class PhotoObjectStorageTests
{
    private static readonly byte[] Original = [1, 2, 3, 4];
    private static readonly byte[] Thumbnail = [9, 9];

    private static ObjectStoreInventoryPhotoBlobStore NewStore(FakePhotoObjects objects, string? prefix = null) =>
        new(objects,
            Options.Create(new PhotoObjectStorageOptions { Bucket = "photos", Prefix = prefix }),
            NullLogger<ObjectStoreInventoryPhotoBlobStore>.Instance);

    private static InventoryPhoto NewPhoto(bool heldInDatabase) => new()
    {
        OrganizationId = Guid.NewGuid(),
        InventoryItemId = Guid.NewGuid(),
        ContentType = "image/webp",
        OriginalBytes = heldInDatabase ? Original : null,
        ThumbnailBytes = heldInDatabase ? Thumbnail : null,
        OriginalByteSize = Original.Length,
        ThumbnailByteSize = Thumbnail.Length,
    };

    private static ProcessedPhoto NewProcessed(string contentType = "image/webp") =>
        new(Original, Thumbnail, contentType, "photo.webp", Original.Length, Thumbnail.Length, 800, 600);

    [Fact]
    public async Task Storing_a_photo_puts_both_images_in_the_bucket_and_empties_the_columns()
    {
        FakePhotoObjects objects = new();
        InventoryPhoto photo = NewPhoto(heldInDatabase: true);

        await NewStore(objects).StoreAsync(photo, NewProcessed(), CancellationToken.None);

        Assert.Equal($"{photo.OrganizationId:N}/{photo.Id:N}/original.webp", photo.OriginalObjectKey);
        Assert.Equal($"{photo.OrganizationId:N}/{photo.Id:N}/thumbnail.jpg", photo.ThumbnailObjectKey);
        Assert.Equal(Original, objects.Stored[photo.OriginalObjectKey!].Bytes);
        Assert.Equal("image/webp", objects.Stored[photo.OriginalObjectKey!].ContentType);
        Assert.Equal("image/jpeg", objects.Stored[photo.ThumbnailObjectKey!].ContentType);

        // The point of the move: the row no longer carries the image.
        Assert.Null(photo.OriginalBytes);
        Assert.Null(photo.ThumbnailBytes);
    }

    [Fact]
    public async Task Keys_are_written_under_the_configured_prefix()
    {
        FakePhotoObjects objects = new();
        InventoryPhoto photo = NewPhoto(heldInDatabase: true);

        await NewStore(objects, prefix: "staging/").StoreAsync(photo, NewProcessed(), CancellationToken.None);

        Assert.StartsWith("staging/", photo.OriginalObjectKey);
        Assert.DoesNotContain("//", photo.OriginalObjectKey);
    }

    [Theory]
    [InlineData("image/jpeg", "original.jpg")]
    [InlineData("image/png", "original.png")]
    [InlineData("image/webp", "original.webp")]
    public async Task An_images_key_keeps_the_extension_its_type_implies(string contentType, string expected)
    {
        FakePhotoObjects objects = new();
        InventoryPhoto photo = NewPhoto(heldInDatabase: true);

        await NewStore(objects).StoreAsync(photo, NewProcessed(contentType), CancellationToken.None);

        Assert.EndsWith(expected, photo.OriginalObjectKey);
    }

    [Fact]
    public async Task A_stored_photo_reads_back_from_the_bucket()
    {
        FakePhotoObjects objects = new();
        ObjectStoreInventoryPhotoBlobStore store = NewStore(objects);
        InventoryPhoto photo = NewPhoto(heldInDatabase: true);
        await store.StoreAsync(photo, NewProcessed(), CancellationToken.None);

        PhotoBytes? original = await store.ReadOriginalAsync(photo, CancellationToken.None);
        PhotoBytes? thumbnail = await store.ReadThumbnailAsync(photo, CancellationToken.None);

        Assert.Equal(Original, original!.Bytes);
        Assert.Equal("image/webp", original.ContentType);
        Assert.Equal(Thumbnail, thumbnail!.Bytes);
        Assert.Equal("image/jpeg", thumbnail.ContentType);
    }

    [Fact]
    public async Task A_photo_not_yet_moved_still_serves_from_its_columns()
    {
        // This is what makes turning object storage on a setting rather than a migration.
        InventoryPhoto photo = NewPhoto(heldInDatabase: true);
        ObjectStoreInventoryPhotoBlobStore store = NewStore(new FakePhotoObjects());

        Assert.Equal(Original, (await store.ReadOriginalAsync(photo, CancellationToken.None))!.Bytes);
        Assert.Equal(Thumbnail, (await store.ReadThumbnailAsync(photo, CancellationToken.None))!.Bytes);
    }

    [Fact]
    public async Task An_object_the_bucket_has_lost_falls_back_to_whatever_the_row_still_holds()
    {
        InventoryPhoto photo = NewPhoto(heldInDatabase: true);
        photo.OriginalObjectKey = "gone/original.webp";

        PhotoBytes? original = await NewStore(new FakePhotoObjects()).ReadOriginalAsync(photo, CancellationToken.None);

        Assert.Equal(Original, original!.Bytes);
    }

    [Fact]
    public async Task An_image_that_exists_nowhere_reads_as_nothing_rather_than_as_empty_bytes()
    {
        InventoryPhoto photo = NewPhoto(heldInDatabase: false);
        photo.OriginalObjectKey = "gone/original.webp";

        Assert.Null(await NewStore(new FakePhotoObjects()).ReadOriginalAsync(photo, CancellationToken.None));
    }

    [Fact]
    public async Task Forgetting_a_photo_deletes_both_of_its_images()
    {
        FakePhotoObjects objects = new();
        ObjectStoreInventoryPhotoBlobStore store = NewStore(objects);
        InventoryPhoto photo = NewPhoto(heldInDatabase: true);
        await store.StoreAsync(photo, NewProcessed(), CancellationToken.None);

        string original = photo.OriginalObjectKey!;
        string thumbnail = photo.ThumbnailObjectKey!;

        await store.ForgetAsync(photo, CancellationToken.None);

        Assert.Equal([original, thumbnail], objects.Deleted);
        Assert.Empty(objects.Stored);
    }

    [Fact]
    public async Task A_bucket_that_will_not_answer_cannot_stop_a_photo_being_deleted()
    {
        FakePhotoObjects objects = new();
        ObjectStoreInventoryPhotoBlobStore store = NewStore(objects);
        InventoryPhoto photo = NewPhoto(heldInDatabase: true);
        await store.StoreAsync(photo, NewProcessed(), CancellationToken.None);
        objects.Refuses = true;

        // No exception: the operator's deletion goes through and the orphaned object is logged instead.
        await store.ForgetAsync(photo, CancellationToken.None);
    }

    [Fact]
    public async Task The_database_store_keeps_images_in_the_row()
    {
        DatabaseInventoryPhotoBlobStore store = new();
        InventoryPhoto photo = NewPhoto(heldInDatabase: false);

        await store.StoreAsync(photo, NewProcessed(), CancellationToken.None);

        Assert.Equal(Original, photo.OriginalBytes);
        Assert.Equal(Thumbnail, photo.ThumbnailBytes);
        Assert.Null(photo.OriginalObjectKey);
        Assert.Equal(Original, (await store.ReadOriginalAsync(photo, CancellationToken.None))!.Bytes);
        Assert.Equal("image/jpeg", (await store.ReadThumbnailAsync(photo, CancellationToken.None))!.ContentType);
    }

    [Theory]
    [InlineData(null, null, null, null)]
    [InlineData("photos", null, "key", "secret")]
    [InlineData("photos", "http://minio.local", "key", "secret")]
    [InlineData("photos", "https://account.r2.cloudflarestorage.com", null, "secret")]
    public void A_half_configured_bucket_is_refused_at_startup(string? bucket, string? serviceUrl, string? key, string? secret)
    {
        PhotoObjectStorageOptions options = new()
        {
            Bucket = bucket,
            ServiceUrl = serviceUrl,
            AccessKeyId = key,
            SecretAccessKey = secret,
        };

        // Nothing configured at all is not a misconfiguration: it means photos stay in the database.
        Assert.Equal(bucket is null, options.DescribeMisconfiguration() is null);
    }

    [Fact]
    public void A_fully_configured_bucket_is_accepted()
    {
        PhotoObjectStorageOptions options = new()
        {
            Bucket = "photos",
            ServiceUrl = "https://account.r2.cloudflarestorage.com",
            AccessKeyId = "key",
            SecretAccessKey = "secret",
        };

        Assert.Null(options.DescribeMisconfiguration());
        Assert.True(options.IsConfigured);
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("haulscout", "haulscout/")]
    [InlineData("/haulscout/", "haulscout/")]
    public void A_prefix_is_used_with_exactly_one_trailing_slash(string? prefix, string expected) =>
        Assert.Equal(expected, new PhotoObjectStorageOptions { Prefix = prefix }.NormalizedPrefix);
}
