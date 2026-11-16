import { useCallback, useEffect, useRef, useState } from 'react'
import { Eraser } from 'lucide-react'
import { Button } from '@/components/ui/button'

/** A box to sign in with a finger, stylus or mouse. Reports the signature as a PNG data URL, or null when empty. */
export function SignaturePad({ onChange, error }: { onChange: (dataUrl: string | null) => void; error?: string }) {
  const canvas = useRef<HTMLCanvasElement>(null)
  const drawing = useRef(false)
  const [empty, setEmpty] = useState(true)

  // Match the drawing surface to the screen's pixel density, so the line stays sharp.
  useEffect(() => {
    const el = canvas.current
    if (!el) return
    const ratio = window.devicePixelRatio || 1
    const { width, height } = el.getBoundingClientRect()
    el.width = Math.round(width * ratio)
    el.height = Math.round(height * ratio)
    const ctx = el.getContext('2d')
    if (!ctx) return
    ctx.scale(ratio, ratio)
    ctx.lineWidth = 2.2
    ctx.lineCap = 'round'
    ctx.lineJoin = 'round'
    ctx.strokeStyle = '#0d1b3e'
  }, [])

  const point = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const rect = e.currentTarget.getBoundingClientRect()
    return { x: e.clientX - rect.left, y: e.clientY - rect.top }
  }

  const start = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const ctx = e.currentTarget.getContext('2d')
    if (!ctx) return
    e.currentTarget.setPointerCapture(e.pointerId)
    drawing.current = true
    const { x, y } = point(e)
    ctx.beginPath()
    ctx.moveTo(x, y)
    // A tap leaves a dot.
    ctx.lineTo(x + 0.1, y + 0.1)
    ctx.stroke()
  }

  const move = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (!drawing.current) return
    const ctx = e.currentTarget.getContext('2d')
    if (!ctx) return
    const { x, y } = point(e)
    ctx.lineTo(x, y)
    ctx.stroke()
  }

  const end = () => {
    if (!drawing.current) return
    drawing.current = false
    setEmpty(false)
    onChange(canvas.current?.toDataURL('image/png') ?? null)
  }

  const clear = useCallback(() => {
    const el = canvas.current
    el?.getContext('2d')?.clearRect(0, 0, el.width, el.height)
    setEmpty(true)
    onChange(null)
  }, [onChange])

  return (
    <div className="grid gap-2">
      <div className={`relative overflow-hidden rounded-lg border bg-white ${error ? 'border-destructive' : 'border-input'}`}>
        <canvas
          ref={canvas}
          className="block h-40 w-full cursor-crosshair touch-none"
          role="img"
          aria-label="Signature box. Draw your signature here."
          onPointerDown={start}
          onPointerMove={move}
          onPointerUp={end}
          onPointerCancel={end}
        />
        {empty && <p className="pointer-events-none absolute inset-0 flex items-center justify-center text-sm text-muted-foreground">Sign here</p>}
        <div aria-hidden className="pointer-events-none absolute inset-x-6 bottom-9 border-b border-dashed border-muted-foreground/40" />
      </div>
      <div className="flex items-center justify-between gap-2">
        {error ? <p className="text-xs text-destructive">{error}</p> : <span />}
        <Button type="button" variant="ghost" size="sm" onClick={clear} disabled={empty}>
          <Eraser /> Clear
        </Button>
      </div>
    </div>
  )
}
