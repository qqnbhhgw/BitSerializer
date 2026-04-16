param(
    [Parameter(Mandatory = $false)]
    [string]$Path = "."
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command rg -ErrorAction SilentlyContinue)) {
    throw "ripgrep (rg) is required by this script."
}

Write-Host "Scanning repository: $Path"

$patterns = @(
    # Package and namespace usage
    'PackageReference\s+Include=.*BinarySerializer',
    'using\s+BinarySerialization\b',
    '\bBinarySerializer\b',
    '\bIBinarySerializable\b',
    '\bIValueConverter\b',

    # Core declarative attributes
    '\[FieldOrder\b',
    '\[FieldLength\b',
    '\[FieldBitLength\b',
    '\[FieldBitOrder\b',
    '\[FieldCount\b',
    '\[FieldAlignment\b',
    '\[FieldScale\b',
    '\[FieldEndianness\b',
    '\[FieldEncoding\b',
    '\[FieldOffset\b',
    '\[FieldValue\b',
    '\[FieldChecksum\b',
    '\[FieldCrc16\b',
    '\[FieldCrc32\b',

    # Conditional and subtype attributes
    '\[Subtype\b',
    '\[SubtypeFactory\b',
    '\[SubtypeDefault\b',
    '\[SerializeAs\b',
    '\[SerializeAsEnum\b',
    '\[SerializeWhen\b',
    '\[SerializeWhenNot\b',
    '\[SerializeUntil\b',
    '\[ItemLength\b',
    '\[ItemSubtype\b',
    '\[ItemSubtypeFactory\b',
    '\[ItemSubtypeDefault\b',
    '\[ItemSerializeUntil\b'
)

$args = @(
    "--hidden",
    "--glob", "!**/.git/**",
    "--glob", "!**/bin/**",
    "--glob", "!**/obj/**",
    "--glob", "!**/skills/binaryserializer-to-bitserializer-migration/**",
    "-n",
    "-S"
)

foreach ($pattern in $patterns) {
    Write-Host ""
    Write-Host "=== $pattern ==="
    rg @args -- $pattern $Path
}
