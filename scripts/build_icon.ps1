# App-Icon-Generator fuer LDAPeek.
#
# Motiv: weisse Personen-Silhouette auf Kroste-Blau, darueber ein goldener
# Lupenring - "Konten nachschlagen". Der Ring liegt bewusst mit einem
# hintergrundfarbenen Aussenrand auf der Silhouette, sonst verschwimmen Weiss
# und Gold an der Kante.
#
# Erzeugt:
#   LDAPeek/Assets/ldapeek.png   (256x256, Master fuer Fenster und Tray)
#   LDAPeek/Assets/ldapeek.ico   (multi-res 16..256 fuer <ApplicationIcon>)
#
# Aufruf aus dem Repo-Root:  pwsh -File scripts/build_icon.ps1
#
# Reines ASCII im Quelltext (Windows-PowerShell-5.1-ANSI-Falle).

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$APP_NAME = 'LDAPeek'

# Kroste-Palette (siehe App.axaml)
$BG     = [System.Drawing.Color]::FromArgb(255, 18, 62, 107)    # #123E6B
$FG     = [System.Drawing.Color]::FromArgb(255, 255, 255, 255)
$FG_DIM = [System.Drawing.Color]::FromArgb(255, 214, 224, 238)
$GOLD   = [System.Drawing.Color]::FromArgb(255, 224, 177, 76)   # #E0B14C

$CORNER = 48

function New-Graphics([System.Drawing.Bitmap]$bmp) {
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    return $g
}

function Add-RoundedRect($g, [float]$x, [float]$y, [float]$w, [float]$h, [float]$r, $brush) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath($brush, $path)
    $path.Dispose()
}

function Add-Background($g, [int]$size, [double]$scale) {
    # Radius IMMER gegen die Kantenlaenge deckeln - sonst ueberlappen sich bei
    # kleinen Groessen die vier Boegen, der Pfad degeneriert und die Ecken
    # zerfallen sichtbar. 48px ist die Standardgroesse im Explorer, ein Fehler
    # dort faellt sofort auf.
    $corner = [Math]::Max(2, [int][Math]::Min($CORNER * $scale, $size * 0.22))
    $b = New-Object System.Drawing.SolidBrush $BG
    Add-RoundedRect $g 0 0 ($size - 1) ($size - 1) $corner $b
    $b.Dispose()
}

function Add-Person($g, [double]$scale, [double]$cx, [double]$headCy, [double]$headR,
                    [double]$bodyTop, [double]$bodyBottom, [double]$bodyHalfWidth, $brush) {
    # Kopf
    $g.FillEllipse($brush, [float]($cx - $headR), [float]($headCy - $headR),
        [float](2 * $headR), [float](2 * $headR))

    # Schultern: oben stark gerundetes Rechteck, unten buendig abgeschnitten.
    $bodyHeight = $bodyBottom - $bodyTop
    $r = [Math]::Min($bodyHalfWidth, $bodyHeight)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc([float]($cx - $bodyHalfWidth), [float]$bodyTop, [float]$d, [float]$d, 180, 90)
    $path.AddArc([float]($cx + $bodyHalfWidth - $d), [float]$bodyTop, [float]$d, [float]$d, 270, 90)
    $path.AddLine([float]($cx + $bodyHalfWidth), [float]$bodyBottom,
                  [float]($cx - $bodyHalfWidth), [float]$bodyBottom)
    $path.CloseFigure()
    $g.FillPath($brush, $path)
    $path.Dispose()
}

function New-IconLarge([int]$size) {
    $scale = $size / 256.0
    $bmp = New-Object System.Drawing.Bitmap $size, $size,
        ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = New-Graphics $bmp
    Add-Background $g $size $scale

    $bFg  = New-Object System.Drawing.SolidBrush $FG
    $bDim = New-Object System.Drawing.SolidBrush $FG_DIM

    # Person leicht nach links oben, damit die Lupe unten rechts Platz hat.
    Add-Person $g $scale (108 * $scale) (86 * $scale) (36 * $scale) `
        (138 * $scale) (208 * $scale) (62 * $scale) $bDim
    # Kopf nochmal in Vollweiss darueber - hebt ihn vom Koerper ab.
    $headR = 36 * $scale
    $g.FillEllipse($bFg, [float](108 * $scale - $headR), [float](86 * $scale - $headR),
        [float](2 * $headR), [float](2 * $headR))

    # Lupe: erst ein hintergrundfarbener Aussenring als Trennung zur Silhouette,
    # dann der goldene Ring darauf.
    $ringCx = 176 * $scale
    $ringCy = 168 * $scale
    $ringR  = 46 * $scale
    $ringW  = [Math]::Max(2, 13 * $scale)

    $penGap  = New-Object System.Drawing.Pen $BG, ([float]($ringW + 8 * $scale))
    $penGold = New-Object System.Drawing.Pen $GOLD, ([float]$ringW)
    $penGold.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $penGold.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $g.DrawEllipse($penGap, [float]($ringCx - $ringR), [float]($ringCy - $ringR),
        [float](2 * $ringR), [float](2 * $ringR))
    $g.DrawEllipse($penGold, [float]($ringCx - $ringR), [float]($ringCy - $ringR),
        [float](2 * $ringR), [float](2 * $ringR))

    # Griff, 45 Grad nach unten rechts.
    $k = [Math]::Sqrt(0.5)
    $hx1 = $ringCx + $ringR * $k
    $hy1 = $ringCy + $ringR * $k
    $hx2 = $ringCx + ($ringR + 30 * $scale) * $k
    $hy2 = $ringCy + ($ringR + 30 * $scale) * $k

    $penGripGap = New-Object System.Drawing.Pen $BG, ([float]($ringW + 8 * $scale))
    $penGripGap.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $penGripGap.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($penGripGap, [float]$hx1, [float]$hy1, [float]$hx2, [float]$hy2)
    $g.DrawLine($penGold, [float]$hx1, [float]$hy1, [float]$hx2, [float]$hy2)

    $penGap.Dispose(); $penGold.Dispose(); $penGripGap.Dispose()
    $bFg.Dispose(); $bDim.Dispose()
    $g.Dispose()
    return $bmp
}

function New-IconSmall([int]$size) {
    # Vereinfachte Variante fuer 16..48px: die Proportionen des Masters gelten
    # dort nicht mehr - Griff und Koerperabsetzung matschen zu einem Fleck.
    # Uebrig bleiben Silhouette und Goldring, und die Wiedererkennung traegt
    # bei 16px ohnehin die Farbkombination, nicht die Form.
    $bmp = New-Object System.Drawing.Bitmap $size, $size,
        ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = New-Graphics $bmp
    Add-Background $g $size 1.0

    $bFg = New-Object System.Drawing.SolidBrush $FG

    $cx      = $size * 0.42
    $headR   = [Math]::Max(2, $size * 0.155)
    $headCy  = $size * 0.34
    $bodyTop = $size * 0.55
    $bodyBot = $size * 0.80
    $bodyHw  = $size * 0.25

    Add-Person $g 1.0 $cx $headCy $headR $bodyTop $bodyBot $bodyHw $bFg

    $ringR = [Math]::Max(3, $size * 0.21)
    $ringW = [Math]::Max(1.5, $size * 0.075)
    $ringCx = $size * 0.68
    $ringCy = $size * 0.66

    $penGap = New-Object System.Drawing.Pen $BG, ([float]($ringW + [Math]::Max(1.5, $size * 0.05)))
    $penGold = New-Object System.Drawing.Pen $GOLD, ([float]$ringW)
    $g.DrawEllipse($penGap, [float]($ringCx - $ringR), [float]($ringCy - $ringR),
        [float](2 * $ringR), [float](2 * $ringR))
    $g.DrawEllipse($penGold, [float]($ringCx - $ringR), [float]($ringCy - $ringR),
        [float](2 * $ringR), [float](2 * $ringR))

    $penGap.Dispose(); $penGold.Dispose(); $bFg.Dispose()
    $g.Dispose()
    return $bmp
}

function New-Icon([int]$size) {
    if ($size -le 48) { return New-IconSmall $size } else { return New-IconLarge $size }
}

function Save-Ico([System.Drawing.Bitmap[]]$images, [string]$path) {
    # System.Drawing kann keine Multi-Res-ICOs schreiben - deshalb das
    # ICO-Format von Hand. Eingebettete PNGs sind ab Windows Vista erlaubt.
    $blobs = foreach ($img in $images) {
        $ms = New-Object System.IO.MemoryStream
        $img.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        , $ms.ToArray()
    }

    $fs = [System.IO.File]::Create($path)
    $bw = New-Object System.IO.BinaryWriter $fs
    try {
        $bw.Write([uint16]0)                  # reserved
        $bw.Write([uint16]1)                  # type: 1 = Icon
        $bw.Write([uint16]$images.Count)

        $offset = 6 + 16 * $images.Count
        for ($i = 0; $i -lt $images.Count; $i++) {
            $w = $images[$i].Width
            # 256 wird im ICO-Header als 0 kodiert.
            $bw.Write([byte]($(if ($w -ge 256) { 0 } else { $w })))
            $bw.Write([byte]($(if ($w -ge 256) { 0 } else { $w })))
            $bw.Write([byte]0)                # Farbanzahl (0 = truecolor)
            $bw.Write([byte]0)                # reserved
            $bw.Write([uint16]1)              # color planes
            $bw.Write([uint16]32)             # bits per pixel
            $bw.Write([uint32]$blobs[$i].Length)
            $bw.Write([uint32]$offset)
            $offset += $blobs[$i].Length
        }
        foreach ($blob in $blobs) { $bw.Write($blob) }
    }
    finally {
        $bw.Dispose(); $fs.Dispose()
    }
}

# Repo-relativ: <repo>/scripts/build_icon.ps1 -> <repo>/LDAPeek/Assets
$assets = Join-Path (Split-Path -Parent $PSScriptRoot) "$APP_NAME/Assets"
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$pngPath = Join-Path $assets "$($APP_NAME.ToLowerInvariant()).png"
$icoPath = Join-Path $assets "$($APP_NAME.ToLowerInvariant()).ico"

$master = New-Icon 256
$master.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Wrote $pngPath (256x256)"

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$icons = foreach ($s in $sizes) { New-Icon $s }
Save-Ico $icons $icoPath
Write-Host "Wrote $icoPath (multi-res: $($sizes -join ', '))"

foreach ($i in $icons) { $i.Dispose() }
$master.Dispose()
