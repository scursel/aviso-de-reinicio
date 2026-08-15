# Gera app.ico e preview.png (icone do RestartReminder)
# Circulo azul com seta circular branca (simbolo de "reiniciar")
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$outDir = $PSScriptRoot

function New-IconPng([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $blue  = [System.Drawing.Color]::FromArgb(31, 94, 255)
    $white = [System.Drawing.Color]::White

    # fundo: circulo azul
    $pad = [Math]::Max(1, [int]($size * 0.07))
    $rect = New-Object System.Drawing.Rectangle($pad, $pad, ($size - 2 * $pad), ($size - 2 * $pad))
    $brush = New-Object System.Drawing.SolidBrush($blue)
    $g.FillEllipse($brush, $rect)
    $brush.Dispose()

    # seta circular branca (arco + ponta de seta)
    $penW = [Math]::Max(2.0, $size * 0.115)
    $pen = New-Object System.Drawing.Pen($white, $penW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round

    $inner = [Math]::Max(2, [int]($size * 0.25))
    $arcRect = New-Object System.Drawing.Rectangle($inner, $inner, ($size - 2 * $inner), ($size - 2 * $inner))
    $g.DrawArc($pen, $arcRect, 40, 285)

    # ponta da seta no fim do arco (angulo final = 325 graus)
    $cx = $size / 2.0
    $r = $arcRect.Width / 2.0
    $ang = 325.0 * [Math]::PI / 180.0
    $tipX = $cx + $r * [Math]::Cos($ang)
    $tipY = $cx + $r * [Math]::Sin($ang)

    # direcao do movimento (tangente, sentido horario na tela)
    $tangX = -[Math]::Sin($ang)
    $tangY =  [Math]::Cos($ang)
    # normal apontando para fora
    $nrmX = [Math]::Cos($ang)
    $nrmY = [Math]::Sin($ang)

    $L = $size * 0.24   # comprimento da ponta
    $W = $size * 0.11   # meia largura da base
    $bX = $tipX - $L * $tangX
    $bY = $tipY - $L * $tangY
    $p1 = New-Object System.Drawing.PointF(($bX + $W * $nrmX), ($bY + $W * $nrmY))
    $p2 = New-Object System.Drawing.PointF(($bX - $W * $nrmX), ($bY - $W * $nrmY))
    $tip = New-Object System.Drawing.PointF($tipX, $tipY)

    $triBrush = New-Object System.Drawing.SolidBrush($white)
    $g.FillPolygon($triBrush, @($tip, $p1, $p2))
    $triBrush.Dispose()
    $pen.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $bytes = $ms.ToArray()
    $ms.Dispose()
    $g.Dispose()
    $bmp.Dispose()
    return ,$bytes
}

$sizes = @(16, 32, 48, 256)
$pngs = @{}
foreach ($s in $sizes) {
    $pngs[$s] = New-IconPng $s
    if ($s -eq 256) {
        [System.IO.File]::WriteAllBytes((Join-Path $outDir 'preview.png'), $pngs[$s])
    }
}

# monta o arquivo .ico (formato PNG-compressed, suportado desde o Vista)
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($ms)
$bw.Write([UInt16]0)                      # reserved
$bw.Write([UInt16]1)                      # type = icon
$bw.Write([UInt16]$pngs.Count)            # count
$offset = 6 + 16 * $pngs.Count
foreach ($s in ($pngs.Keys | Sort-Object)) {
    $data = $pngs[$s]
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]0)                    # colors
    $bw.Write([byte]0)                    # reserved
    $bw.Write([UInt16]1)                  # planes
    $bw.Write([UInt16]32)                 # bpp
    $bw.Write([UInt32]$data.Length)       # size in bytes
    $bw.Write([UInt32]$offset)            # offset
    $offset += $data.Length
}
foreach ($s in ($pngs.Keys | Sort-Object)) { $bw.Write($pngs[$s]) }
$bw.Flush()
[System.IO.File]::WriteAllBytes((Join-Path $outDir 'app.ico'), $ms.ToArray())
$bw.Dispose()
$ms.Dispose()

Write-Output "OK: $(Join-Path $outDir 'app.ico')"