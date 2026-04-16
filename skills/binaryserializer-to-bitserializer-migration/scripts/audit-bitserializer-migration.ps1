param(
    [Parameter(Mandatory = $false)]
    [string]$Path = "."
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command rg -ErrorAction SilentlyContinue)) {
    throw "ripgrep (rg) is required by this script."
}

Write-Host "Auditing BitSerializer migration residues under: $Path"
Write-Host "Goal: find manual fallbacks that the declarative API can now replace."
Write-Host ""

$checks = @(
    @{
        Name        = "Manual CRC in AfterSerialize / BeforeSerialize hook (consider [BitCrc] + [BitCrcInclude])"
        Pattern     = '(AfterSerialize|BeforeSerialize)\s*\([^)]*\)\s*\{[^}]*(Crc|CRC|Checksum)'
        Multiline   = $true
    },
    @{
        Name        = "UartCrc16 / CrcCcitt / Crc32 manual invocations with hardcoded byte offsets (move to [BitCrc])"
        Pattern     = '(UartCrc16|Crc16|Crc32|CrcCcitt)\s*\([^)]*,\s*\d+\s*,\s*[^)]*\)'
    },
    @{
        Name        = "BinaryPrimitives.Write*BigEndian/LittleEndian inside AfterSerialize (CRC-writeback pattern)"
        Pattern     = 'BinaryPrimitives\.(Write|Read)U?Int(16|32|64)(Big|Little)Endian'
    },
    @{
        Name        = "IBitSerializable manual implementations (may be replaceable by [BitSerialize] + new attributes)"
        Pattern     = ':\s*IBitSerializable\b'
    },
    @{
        Name        = "Manual zero-padding of short byte arrays (replace with [BitFieldCount(N, PadIfShort = true)])"
        Pattern     = '(Array\.Copy|Buffer\.BlockCopy|CopyTo)\s*\([^)]*\)\s*;[\s\S]{0,80}?(Array\.Clear|new byte\[\d+\])'
        Multiline   = $true
    },
    @{
        Name        = "Read-to-end byte loops (consider [BitFieldConsumeRemaining])"
        Pattern     = 'while\s*\([^)]*(\.Position|bitOffset)\s*<\s*[^)]*(\.Length|bytes\.Length)'
    },
    @{
        Name        = "BinarySerialization namespace still imported (migration incomplete)"
        Pattern     = 'using\s+BinarySerialization\b'
    },
    @{
        Name        = "FieldCrc16/FieldCrc32 attribute still present (migration incomplete)"
        Pattern     = '\[FieldCrc(16|32)\b'
    }
)

$commonArgs = @(
    "--hidden",
    "--glob", "!**/.git/**",
    "--glob", "!**/bin/**",
    "--glob", "!**/obj/**",
    "--glob", "!**/skills/binaryserializer-to-bitserializer-migration/**",
    "-n",
    "-S"
)

$foundAny = $false

foreach ($check in $checks) {
    $args = $commonArgs
    if ($check.ContainsKey("Multiline") -and $check["Multiline"]) {
        $args = $args + @("-U", "--multiline-dotall")
    }
    Write-Host ""
    Write-Host "=== $($check.Name) ==="
    $output = rg @args -- $check.Pattern $Path
    if ($LASTEXITCODE -eq 0 -and $output) {
        $foundAny = $true
        Write-Host $output
    } else {
        Write-Host "(no matches)"
    }
}

Write-Host ""
if ($foundAny) {
    Write-Host "AUDIT: review matches above. Many can migrate to declarative [BitCrc], PadIfShort, or [BitFieldConsumeRemaining]."
    exit 1
} else {
    Write-Host "AUDIT CLEAN: no obvious manual fallback residues found."
    exit 0
}
