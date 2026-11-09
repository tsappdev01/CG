namespace CGTOOL.Web.Data.Governance;

/// <summary>How large an uploaded file may be, and how that is said on screen.
///
/// One definition because the number and the sentence had drifted apart before: the limit lived in
/// a const on each page while "up to 1 MB" was typed into the markup six times, so raising one
/// meant finding all seven. A screen that enforces 5 MB while telling the member 1 MB is worse
/// than either number on its own.
/// </summary>
public static class UploadLimits
{
    /// <summary>Identity and supporting documents a member uploads -- Emirates ID, passport,
    /// trade licence, and the types the Document Centre adds.
    ///
    /// Was 1 MB, from Functional Spec section 3.3. A phone photograph of an Emirates ID is
    /// routinely larger than that, so members were scaling images down to get past the check,
    /// which is the opposite of what a legible copy of an identity document is for.</summary>
    public const long DocumentBytes = 5 * 1024 * 1024;

    /// <summary>Supporting material a member or an officer attaches: a scanned agreement, board
    /// minutes, a trade licence, anything the Document Centre asks for. Larger than an identity
    /// document because these are multi-page scans produced by an office, not a phone.</summary>
    public const long SupportingDocumentBytes = 10 * 1024 * 1024;

    /// <summary>Bulk and administrative uploads: spreadsheets, policy documents, register
    /// extracts. Deliberately higher -- these are files the organisation produces, not a
    /// photograph someone takes on a phone.</summary>
    public const long BulkBytes = 20 * 1024 * 1024;

    public static string SupportingDocumentLimit => Describe(SupportingDocumentBytes);

    public static string BulkLimit => Describe(BulkBytes);

    /// <summary>A profile picture or an entity logo. Not a document: it is displayed small and
    /// stored for every member, so it stays the smallest of the tiers.</summary>
    public const long PictureBytes = 5 * 1024 * 1024;

    public static string PictureLimit => Describe(PictureBytes);

    public static string Describe(long bytes) => $"{bytes / (1024 * 1024)} MB";

    /// <summary>The limit as a member reads it, for a hint line.</summary>
    public static string DocumentLimit => Describe(DocumentBytes);

    /// <summary>What a card says when nothing has been uploaded into it yet.</summary>
    public static string DocumentHint => $"JPG, PNG or PDF, up to {DocumentLimit}";

    /// <summary>What the member is told when the file is too big. Names the file's own size, so
    /// "too large" is answerable rather than something to guess at.</summary>
    public static string TooLarge(long size, long limit = DocumentBytes) =>
        size >= 1024 * 1024
            ? $"That file is {size / (double)(1024 * 1024):0.#} MB. The limit is {Describe(limit)}."
            : $"That file is over the {Describe(limit)} limit.";
}
