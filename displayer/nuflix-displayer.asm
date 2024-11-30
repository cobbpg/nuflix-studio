.segment MAIN [outBin="nufli-template.bin"]

.label VideoStandard = $02a6 // NTSC - 0, PAL - 1
.label IrqVector = $0314
.label FinishIrq = $ea81
.label SpeedCode = $1000
.label SpeedCodeFliDeltas = $40a0
.label SpeedCodeFixVars = $e0
.label SpeedCodeSrc = $e0
.label SpeedCodeDst = $e2
.label SpeedCodeFixIndex = $e4
.label SlowValues = $d1

* = $2000 "Template Pointers"
	.word WaitLoop
	.word SwitchBankValueAddress_Pal
	.word SpriteMulti1_InitColor_Pal
	.word SpriteMulti2_InitColor_Pal
	.word Sprite0_InitColor_Pal
	.word Sprite1_InitColor_Pal
	.word Sprite2_InitColor_Pal
	.word Sprite3_InitColor_Pal
	.word Sprite4_InitColor_Pal
	.word Sprite5_InitColor_Pal
	.word Sprite6_InitColor_Pal
	.word Sprite7_InitColor_Pal
	.word RegisterX_InitValue_Pal
	.word RegisterY_InitValue_Pal
	.word SpriteMulti1_InitColor_Ntsc
	.word SpriteMulti2_InitColor_Ntsc
	.word Sprite0_InitColor_Ntsc
	.word Sprite1_InitColor_Ntsc
	.word Sprite2_InitColor_Ntsc
	.word Sprite3_InitColor_Ntsc
	.word Sprite4_InitColor_Ntsc
	.word Sprite5_InitColor_Ntsc
	.word Sprite6_InitColor_Ntsc
	.word Sprite7_InitColor_Ntsc
	.word RegisterX_InitValue_Ntsc
	.word RegisterY_InitValue_Ntsc
	.word SpeedCodeFixCall
	.word TopBorderColor
	.word TopBackgroundColor
* = $2100 "Bottom 1 Underlay"
* = $2280 "Bottom 1 Screen RAM" // 360 bytes
PicB1_ScreenRam:
* = $23f8 "Bottom 1 Sprite Pointers"
PicB1_SpritePtrs:
	.byte $a0,$84,$85,$86,$87,$88,$89,$90
* = $2400 "Bottom Bug Sprite 7"
* = $2500 "Bottom 2 Underlay"
* = $2680 "Bottom 2 Screen RAM" // 360 bytes
* = $27f8 "Bottom 2 Sprite Pointers"
	.byte $a1,$94,$95,$96,$97,$98,$99,$91
* = $2800 "Bottom Bug Sprite 0"
* = $2900 "Bottom 3 Underlay"
* = $2a80 "Bottom 3 Screen RAM" // 360 bytes
* = $2bf8 "Bottom 3 Sprite Pointers"
	.byte $a2,$a4,$a5,$a6,$a7,$a8,$a9,$92
* = $2c00 "Speed Code Fixes for NTSC"
SpeedCodeFixesForNtsc:
	.fill $fb,$00
SpeedCodeFixParams:
	.word $00 // PAL rts address
	.word $00 // NTSC rts address
	.byte $00 // Number of fixes applied
* = $2d00 "Bottom 4 Underlay"
* = $2e80 "Bottom 4 Screen RAM" // 360 bytes
* = $2ff8 "Bottom 4 Sprite Pointers"
	.byte $a3,$b4,$b5,$b6,$b7,$b8,$b9,$93

* = $3000 "Init"

Start:
	ldx VideoStandard
	sei
	lda #$01
	sta $dc0d
	lda IrqLow,x
	sta IrqVector
	lda IrqHigh,x
	sta IrqVector+1
SpeedCodeFixCall:
	jsr FixSpeedCodeForNtsc
	ldx #$27
InitLoop:
	cpx #$08
	bcs InitSpritePtrsDone
	lda PicB1_SpritePtrs,x
	sta $03f8,x
	lda #$00
	sta $7ff8,x
	lda #(ClearSpriteBottom >> 6)
	sta $07f8,x
InitSpritePtrsDone:
	cpx #$20
	bcs InitSlowValuesDone
	lda SlowValuesInit,x
	sta SlowValues,x
InitSlowValuesDone:
	lda VicInitValues,x
	sta $d000,x
	lda PicB1_ScreenRam,x
	sta $0280,x
	dex
	bpl InitLoop
	lda $dc0d
	cli
	jmp WaitLoop

IrqLow:
	.byte <IrqNtsc1, <IrqPal1
IrqHigh:
	.byte >IrqNtsc1, >IrqPal1

IrqPal1:
	lda #$2a
	sta $d012
	lda #<IrqPal2
	sta IrqVector
	inc $d019
	cli
IP_Wait:
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	jmp IP_Wait

IrqPal2:
	tsx
	txa
	clc
	adc #$06
	tax
	txs
	ldy #$ff
	lda $dc01,y
	and #$fc
	ldx $d012
	cpx #$2a
	beq SetupFramePal
SetupFramePal:
	sty $d018 // first cycle: $2b/$06, write cycle: $2b/$09
	ora #$02
	sta $dd00
	ldx #$2b
	stx $d001
	stx $d003
	stx $d005
	stx $d007
	stx $d009
	stx $d00b
	stx $d00d
	stx $d00f // last write at $2b/31, sprite DMA stuns CPU at $2b/$36
	ldx Sprite0_InitColor_Pal: #8
	stx $d027
	ldx Sprite1_InitColor_Pal: #1
	stx $d028
	ldx Sprite2_InitColor_Pal: #2
	stx $d029
	ldx Sprite3_InitColor_Pal: #3
	stx $d02a
	ldx Sprite4_InitColor_Pal: #4
	stx $d02b
	ldx Sprite5_InitColor_Pal: #5
	stx $d02c
	ldx Sprite6_InitColor_Pal: #6
	stx $d02d
	ldx Sprite7_InitColor_Pal: #7
	stx $d02e
	ldx #$aa
	stx $d001
	stx $d003
	stx $d005
	stx $d007
	stx $d009
	stx $d00b
	stx $d00d
	stx $d00f
	ora #$01
	sta SwitchBankValueAddress_Pal: $ffff
	lda #$00 // first cycle: $2d/$32
	sty $d017 // first cycle: $2d/$34, write cycle: $2d/$37
	sta $d018,y // first cycle: $2e/$0b, write cycle: $2e/$0f
	sty $d017 // first cycle: $2e/$10, write cycle: $2e/$13
	lda SpriteMulti2_InitColor_Pal: #9
	sta $d026
	lda SpriteMulti1_InitColor_Pal: #10
	sta $d025
	ldy #8
SetupWaitPal:
	dey
	bne SetupWaitPal
	nop
	nop
	nop
	lda #$78
	sta $d018
	lda #$38
	ldx RegisterX_InitValue_Pal: #0
	ldy RegisterY_InitValue_Pal: #0
	jsr SpeedCode
	lda #$00
	sta $d017
	lda #$29
	sta $d012
	lda #<IrqPal1
	sta IrqVector
	inc $d019
	jmp FinishIrq

IrqNtsc1:
	lda #$2a
	sta $d012
	lda #<IrqNtsc2
	sta IrqVector
	inc $d019
	cli
IN_Wait:
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	nop
	jmp IN_Wait

IrqNtsc2:
	tsx
	txa
	clc
	adc #$06
	tax
	txs
	ldy #$ff
	lda $dc01,y
	nop
	nop
	ldx $d012
	cpx #$2a
	beq SetupFrameNtsc
SetupFrameNtsc:
	sty $d018 // first cycle: $2b/$06, write cycle: $2b/$09
	lda #$02
	sta $dd00
	ldx #$2b
	stx $d001
	stx $d003
	stx $d005
	stx $d007
	stx $d009
	stx $d00b
	stx $d00d
	stx $d00f // last write at $2b/31, sprite DMA stuns CPU at $2b/$37 (!)
	ldx Sprite0_InitColor_Ntsc: #8
	stx $d027
	ldx Sprite1_InitColor_Ntsc: #1
	stx $d028
	ldx Sprite2_InitColor_Ntsc: #2
	stx $d029
	ldx Sprite3_InitColor_Ntsc: #3
	stx $d02a
	ldx Sprite4_InitColor_Ntsc: #4
	stx $d02b
	ldx Sprite5_InitColor_Ntsc: #5
	stx $d02c
	ldx Sprite6_InitColor_Ntsc: #6
	stx $d02d
	ldx Sprite7_InitColor_Ntsc: #7
	stx $d02e
	ldx #$aa
	stx $d001
	stx $d003
	stx $d005
	stx $d007
	stx $d009
	stx $d00b
	stx $d00d
	stx $d00f
	nop
	nop
	nop
	sty $d017 // first cycle: $2d/$2e, write cycle: $2d/$31
	lda #$00 // first cycle: $2d/$32
	ldx $d017,y // nop5, first cycle: $2d/$34, last cycle: $2e/$0a
	sta $d018,y // first cycle: $2e/$0b, write cycle: $2e/$0f
	sty $d017 // first cycle: $2e/$10, write cycle: $2e/$13
	lda SpriteMulti2_InitColor_Ntsc: #9
	sta $d026
	lda SpriteMulti1_InitColor_Ntsc: #10
	sta $d025
	ldy #8
SetupWaitNtsc:
	dey
	bne SetupWaitNtsc
	nop
	nop
	nop
	lda #$78 // expected: $2f/$23
	sta $d018 // first cycle: $2f/$28, write cycle: $2f/$2b
	lda #$38
	ldx RegisterX_InitValue_Ntsc: #0
	ldy RegisterY_InitValue_Ntsc: #0
	jsr SpeedCode
	lda #$00
	sta $d017
	lda #$29
	sta $d012
	lda #<IrqNtsc1
	sta IrqVector
	inc $d019
	jmp FinishIrq

FixSpeedCodeForNtsc:
	cpx #0
	beq StartFix
	rts
StartFix:
	ldx #4
InitFix:
	lda SpeedCodeFixParams,x
	sta SpeedCodeFixVars,x
	dex
	bpl InitFix
	ldy #0
	ldx #1
	jsr CopySpeedCodeRun
ProcessFix:
	ldx SpeedCodeFixIndex
	lda SpeedCodeFixesForNtsc,x
	cmp #$40
	bcs InsertSimpleDelay
AddExtraCycleToFliTrigger:
	tax // Replace sta $d011 with sta $d011-delta,x
	sec
	lda #$11
	sbc SpeedCodeFliDeltas,x
	pha
	lda #$d0
	sbc #$00
	jsr EmitByte
	pla
	jsr EmitByte
	lda #$9d // sta abs,x
	jsr EmitByte
	sec
	lda SpeedCodeSrc
	sbc #$03
	sta SpeedCodeSrc
	bcs CheckFixIndex
	dec SpeedCodeSrc+1
	jmp CheckFixIndex
InsertSimpleDelay:
	pha
	and #$3f
	beq InsertDelay
	tax
	jsr CopySpeedCodeRun
InsertDelay:
	lda #$ea
	jsr EmitByte
	pla
	rol
	rol
	rol
	and #$03
	tax
	lda DelaySecondBytes,x
	beq CheckFixIndex
	jsr EmitByte
CheckFixIndex:
	lda SpeedCodeFixIndex
	beq FinishFix
	dec SpeedCodeFixIndex
	jmp ProcessFix
FinishFix:
	ldx SpeedCodeSrc
	jsr CopySpeedCodeRun
	rts

DelaySecondBytes:
	.byte $00,$00,$24,$ea // nop / bit $ea / 2 nops

CopySpeedCodeRun:
	lda (SpeedCodeSrc),y
	sta (SpeedCodeDst),y
	lda SpeedCodeSrc
	bne !+
	dec SpeedCodeSrc+1
!:	dec SpeedCodeSrc
	lda SpeedCodeDst
	bne !+
	dec SpeedCodeDst+1
!:	dec SpeedCodeDst
	dex
	bne CopySpeedCodeRun
	rts

EmitByte:
	sta (SpeedCodeDst),y
	lda SpeedCodeDst
	bne !+
	dec SpeedCodeDst+1
!:	dec SpeedCodeDst
	rts

* = $32c0 "Shared Bitmap" // Only every 8th byte, just the bottom row

* = $3400 "Bottom Bitmap"
BitmapB:
	.fill $b40,$00
* = $3f80 "Bottom Clear Sprite"
ClearSpriteBottom:
	.fill $40,$00

* = $4000 "Top 1 Even Screen RAM" // 16 lines, odd ones unused

	.fill $4050-*,$00
VicInitValues:
	.byte $18,$2b,$30,$2b,$60,$2b,$90,$2b,$c0,$2b,$f0,$2b,$20,$2b,$18,$2b
	.byte $40,$78,$29,$00,$00,$ff,$08,$00,$14,$00,$01,$ff,$80,$7e,$00,$00

	.fill $40f0-*,$00
WaitLoop:
!:	bit $d011
	bpl !-
!:	bit $d011
	bmi !-
	lda TopBorderColor: #0
	sta $d020
	lda TopBackgroundColor: #0
	sta $d021
	jmp WaitLoop

	.fill $4140-*,$00
SlowValuesInit:
	.byte $00,$01,$02,$03,$04,$05,$06,$07,$08,$09,$0a,$0b,$0c,$0d,$0e,$0f
	.byte $10,$18,$28,$38,$3a,$3c,$3e,$48,$58,$68,$78,$88,$98,$a8,$b8,$d4

* = $4280 "Top 1 Even Underlay 1-5" // 320 bytes
* = $43f8 "Top 1 Even Sprite Pointers"
	.byte $e0,$0a,$0b,$0c,$0d,$0e,$d0,$d8
* = $4400 "Top 2 Even Screen RAM" // 16 lines, odd ones unused
* = $4680 "Top 2 Even Underlay 1-5" // 320 bytes
* = $47f8 "Top 2 Even Sprite Pointers"
	.byte $e1,$1a,$1b,$1c,$1d,$1e,$d1,$d9
* = $4800 "Top 3 Even Screen RAM" // 16 lines, odd ones unused
* = $4a80 "Top 3 Even Underlay 1-5" // 320 bytes
* = $4bf8 "Top 3 Even Sprite Pointers"
	.byte $e2,$2a,$2b,$2c,$2d,$2e,$d2,$da
* = $4c00 "Top 4 Even Screen RAM" // 16 lines, odd ones unused
* = $4e80 "Top 4 Even Underlay 1-5" // 320 bytes
* = $4ff8 "Top 4 Even Sprite Pointers"
	.byte $e3,$3a,$3b,$3c,$3d,$3e,$d3,$db
* = $5000 "Top 1 Odd Screen RAM" // 16 lines, even ones unused
* = $5280 "Top 1 Odd Underlay 1-5" // 320 bytes
* = $53f8 "Top 1 Odd Sprite Pointers"
	.byte $e4,$4a,$4b,$4c,$4d,$4e,$d4,$dc
* = $5400 "Top 2 Odd Screen RAM" // 16 lines, even ones unused
* = $5680 "Top 2 Odd Underlay 1-5" // 320 bytes
* = $57f8 "Top 2 Odd Sprite Pointers"
	.byte $e5,$5a,$5b,$5c,$5d,$5e,$d5,$dd
* = $5800 "Top 3 Odd Screen RAM" // 16 lines, even ones unused
* = $5a80 "Top 3 Odd Underlay 1-5" // 320 bytes
* = $5bf8 "Top 3 Odd Sprite Pointers"
	.byte $e6,$6a,$6b,$6c,$6d,$6e,$d6,$de
* = $5c00 "Top 4 Odd Screen RAM" // 16 lines, even ones unused
* = $5e80 "Top 4 Odd Underlay 1-5" // 320 bytes
* = $5ff8 "Top 4 Odd Sprite Pointers"
	.byte $e7,$7a,$7b,$7c,$7d,$7e,$d7,$df
* = $6000 "Top Bitmap"
* = $7400 "Top Underlay 6"
* = $7600 "Top Bug Sprite 7"
* = $7800 "Top Bug Sprite 0"

* = $79ff "End"
	.byte 0