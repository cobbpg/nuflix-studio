.segment MAIN [outBin="nufli-template.bin"]

.label VideoStandard = $02a6 // NTSC - 0, PAL - 1
.label IrqVector = $0314
.label FinishIrq = $ea81
.label SpeedCode = $1000

.label SlowScrAdr = $3a
.label Ptr = $fb

* = $2000 "Bottom Bug Sprite 7"
	.byte 0
* = $2100 "Bottom 1 Underlay"
* = $2280 "Bottom 1 Screen RAM" // 360 bytes
PicB1_ScreenRam:
* = $23f8 "Bottom 1 Sprite Pointers"
PicB1_SpritePtrs:
	.byte $c8,$84,$85,$86,$87,$88,$89,$80
* = $2400 "Register Update 1" // 101 bytes
Sprite1_Colors:
	.fill 101, i >= 64 && i < 72 ? (i - 64) * 2 + $11 : 1 // Inserting necessary sprite Y updates
* = $2480 "Register Update 2" // 101 bytes
Sprite2_Colors:
	.fill 101, 2
* = $2500 "Bottom 2 Underlay"
* = $2680 "Bottom 2 Screen RAM" // 360 bytes
* = $27f8 "Bottom 2 Sprite Pointers"
	.byte $c9,$94,$95,$96,$97,$98,$99,$81
* = $2800 "Register Update 3" // 101 bytes
Sprite3_Colors:
	.fill 101, 3
* = $2880 "Register Update 4" // 101 bytes
Sprite4_Colors:
	.fill 101, 4
* = $2900 "Bottom 2 Underlay"
* = $2a80 "Bottom 2 Screen RAM" // 360 bytes
* = $2bf8 "Bottom 2 Sprite Pointers"
	.byte $ca,$a4,$a5,$a6,$a7,$a8,$a9,$82
* = $2c00 "Register Update 5" // 101 bytes
Sprite5_Colors:
	.fill 101, 5
* = $2c80 "Register Update 6" // 101 bytes
Sprite6_Colors:
	.fill 101, 6
* = $2d00 "Bottom 2 Underlay"
* = $2e80 "Bottom 2 Screen RAM" // 360 bytes
* = $2ff8 "Bottom 2 Sprite Pointers"
	.byte $cb,$b4,$b5,$b6,$b7,$b8,$b9,$83

* = $3000 "Init"

Start:
	sei
	lda #$01
	jsr PatchVideoStandard
	lda #<StabiliseIrq1
	sta IrqVector
	lda #>StabiliseIrq1
	sta IrqVector+1
	ldx #$2f
InitVic:
	lda VicInitValues,x
	sta $d000,x
	dex
	bpl InitVic
	lda #<SpeedCode // NTSC: $c0
	sta Ptr
	lda #>SpeedCode // NTSC: $0f
	sta Ptr+1
	ldx #$00
GenerateSpeedCode_Loop:
	txa
	and #$03
	tay
	lda LoopTemplateVal1Offsets,y
	sta GSC_LoopTemplateVal1Offset+1
	lda LoopTemplateSourceOffsets,y
	sta GSC_LoopTemplateSourceOffset+1
	lda FilledTemplateStartOffsets,y
	sta GSC_FilledTemplateStartOffset+1
	tay
GSC_LoopTemplateSourceOffset:
	lda LoopTemplate,y
	sta LoopTemplate,y // NTSC: $32fe
	iny
	cpy #$07
	bne GSC_LoopTemplateSourceOffset
	lda Sprite1_Colors+1,x
	ldy #$28
	jsr DetermineTargetRegister
GSC_LoopTemplateVal1Offset:
	sta LoopTemplate
	sty LoopTemplate_Reg1+1
	lda Sprite2_Colors+1,x
	ldy #$29
	jsr DetermineTargetRegister
	sta LoopTemplate_Val2+1
	sty LoopTemplate_Reg2+1
	lda Sprite3_Colors+1,x
	ldy #$2a
	jsr DetermineTargetRegister
	sta LoopTemplate_Val3+1
	sty LoopTemplate_Reg3+1
	lda Sprite4_Colors+1,x
	ldy #$2b
	jsr DetermineTargetRegister
	sta LoopTemplate_Val4+1
	sty LoopTemplate_Reg4+1
	lda Sprite5_Colors+1,x
	ldy #$2c
	jsr DetermineTargetRegister
	sta LoopTemplate_Val5+1
	sty LoopTemplate_Reg5+1
	lda Sprite6_Colors+1,x
	ldy #$2d
	jsr DetermineTargetRegister
	sta LoopTemplate_Val6+1
	sty LoopTemplate_Reg6+1
	lda ScreenAddressValues,x
	sta LoopTemplate_ScreenAddress+1
	stx $02
GSC_FilledTemplateStartOffset:
	ldx #$00
	ldy #$00
CopyFilledTemplate_Loop:
	lda LoopTemplate,x // NTSC: $32fe
	sta (Ptr),y
	iny
	inx
	cpx #$28 // NTSC: $2a
	bne CopyFilledTemplate_Loop
	tya
	clc
	adc Ptr
	bcc GSC_NextPage
	inc Ptr+1
GSC_NextPage:
	sta Ptr
	ldx $02
	inx
	cpx #$64
	beq GSC_CopyFinalSnippet
	jmp GenerateSpeedCode_Loop

GSC_CopyFinalSnippet:
	ldy #$05
CopyFinalSnippet_Loop:
	lda FinalSnippet,y
	sta (Ptr),y
	dey
	bpl CopyFinalSnippet_Loop
	lda #$00 // Replace $d018 with $dd00
	sta $19be // NTSC: $19fe
	lda #$dd
	sta $19bf // NTSC: $19ff
	lda #$3a
	sta SlowScrAdr
	lda #$a0
	sta $1026 // NTSC: bit (nop)
	ldx #$27
CopyB1ScreenRam_Loop:
	lda PicB1_ScreenRam,x
	sta $0280,x
	lda SharedBitmapPart,x
	sta BitmapB-40,x
	dex
	bpl CopyB1ScreenRam_Loop
	ldx #$07
CopyB1SpritePtrs_Loop:
	lda PicB1_SpritePtrs,x
	sta $03f8,x
	lda #$00
	sta $7ff8,x
	lda #$fe
	sta $07f8,x
	dex
	bpl CopyB1SpritePtrs_Loop
	lda $dc0d
	cli
WaitLoop:
	jmp WaitLoop

StabiliseIrq1:
	lda #$2a
	sta $d012
	lda #<StabiliseIrq2
	sta IrqVector
	inc $d019
	cli
SI_Wait:
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
	jmp SI_Wait

StabiliseIrq2:
	tsx
	txa
	clc
	adc #$06
	tax
	txs
	ldy #$ff
	lda $dc01,y
	and #$fc // NTSC: ldx $xx,y (nop)
	ldx $d012
	cpx #$2a
	beq SetupFrame
SetupFrame:
	sty $d018
	ora #$02 // NTSC: lda
	sta $dd00
	ldx #$2b
	stx $d001
	stx $d003
	stx $d005
	stx $d007
	stx $d009
	stx $d00b
	stx $d00d
	stx $d00f
	ldx Sprite1_Colors
	stx $d028
	ldx Sprite2_Colors
	stx $d029
	ldx Sprite3_Colors
	stx $d02a
	ldx Sprite4_Colors
	stx $d02b
	ldx Sprite5_Colors
	stx $d02c
	ldx Sprite6_Colors
	stx $d02d
	ldx #$aa
	stx $d001
	stx $d003
	stx $d005
	stx $d007
	stx $d009
	stx $d00b
	stx $d00d
	stx $d00f
	ora #$01 // NTSC: cmp ($xx,x) (nop)
	sta $19bc // NTSC: sty $d017
	lda #$00
	sty $d017 // NTSC: ldx $xxxx,y (nop)
	sta $d018,y
	sty $d017
	ldy #$05
SF_Delay:
	dey
	bne SF_Delay
	lda VicInit26
	sta $d026
	lda VicInit25
	sta $d025
	lda VicInit27
	sta $d027
	lda VicInit2e
	sta $d02e
	bit $00 // NTSC: cmp $xx,x
	lda #$38
	ldy #$78
	sty $d018
	ldx #$3e
	jsr SpeedCode // NTSC: $0fc0
	lda #$00
	sta $d017
	lda #$29
	sta $d012
	lda #<StabiliseIrq1
	sta IrqVector
	inc $d019
	jmp FinishIrq

* = $3200 "Bottom Bug Sprite 0"
BugB_Sprite0:

* = $3300 "Speedcode Template"
LoopTemplate:
	.byte $00
	.byte $00
	.byte $00
	.byte $00
	.byte $00
	.byte $00 // NTSC: cmp $xx,x
	.byte $00

LoopTemplate_Reg1:
	sty $d028
LoopTemplate_Val2:
	ldy #$00
LoopTemplate_Reg2:
	sty $d029
LoopTemplate_Val3:
	ldy #$00
LoopTemplate_Reg3:
	sty $d02a
LoopTemplate_Val4:
	ldy #$00
LoopTemplate_Reg4:
	sty $d02b
LoopTemplate_Val5:
	ldy #$00
LoopTemplate_Reg5:
	sty $d02c
LoopTemplate_Val6:
	ldy #$00
LoopTemplate_Reg6:
	sty $d02d
LoopTemplate_ScreenAddress:
	ldy #$00
	sty $d018
	ldy SlowScrAdr // NTSC: ldy $fc,x
	sty $d011
	ldy #$00
	ldy #$3c
	sty $d011
	ldy #$00
	ldy #$00
	stx $d011
	sta $d011
	ldy #$00
FilledTemplateStartOffsets:
	.byte $02,$00,$00,$02
LoopTemplateSourceOffsets:
	.byte $39,$28,$2f,$34
LoopTemplateVal1Offsets:
	.byte $06,$06,$06,$03 // NTSC: 4, 4, 4, 1
ScreenAddressValues:
	.byte $68,$58,$48,$38,$28,$18,$08,$78,$68,$58,$48,$38,$28,$18,$08,$78
	.byte $68,$58,$48,$38,$28,$18,$08,$78,$68,$58,$48,$38,$28,$18,$08,$78
	.byte $68,$58,$48,$38,$28,$18,$08,$78,$68,$58,$48,$38,$28,$18,$08,$78
	.byte $68,$58,$48,$38,$28,$18,$08,$78,$68,$58,$48,$38,$28,$18,$08,$03
	.byte $98,$a8,$b8,$88,$98,$a8,$b8,$88,$98,$a8,$b8,$88,$98,$a8,$b8,$88
	.byte $98,$a8,$b8,$98,$a8,$b8,$88,$98,$a8,$b8,$88,$98,$a8,$b8,$88,$98
	.byte $a8,$b8,$88,$18

DetermineTargetRegister:
	sta $03
	lsr
	lsr
	lsr
	lsr
	beq DTR_Done
	cmp #$01
	bne DTR_PickRegister
	lda $03 // $1x: update sprite (x >> 1) Y coordinate
	and #$0f
	tay
	lda #$d4 // New Y value
	rts

DTR_PickRegister:
	cmp #$02
	bne DTR_NotBorder
	lda #$00 // $2x: border colour
DTR_NotBorder:
	ora #$20 // $5x, $6x: MC
	tay
DTR_Done:
	lda $03
	rts

FinalSnippet:
	ldy #$10
	sty $d011
	rts

* = $3400 "Bottom Bitmap"
BitmapB:
	.fill $b40, 0

PatchVideoStandard:
	sta $dc0d
	ldx #>InitDataValues_NTSC
	lda VideoStandard
	beq PVS_PickStandard
	ldx #>InitDataValues_PAL
PVS_PickStandard:
	stx PVS_InitDataSource+2
	ldx #$00
	stx Ptr
PVS_SetPatchPage:
	sta Ptr+1
PVS_Loop:
	inx
PVS_InitDataSource:
	lda InitDataValues_NTSC-1,x
	ldy InitDataOffsets-1,x
	beq PVS_SetPatchPage
	sta (Ptr),y
	cpx #$1e
	bne PVS_Loop
	rts

* = $3f80 "Bottom Clear Sprite"
ClearSpriteBottom:
	.fill $40, 0
VicInitValues:
	.byte $18,$2b,$30,$2b,$60,$2b,$90,$2b,$c0,$2b,$f0,$2b,$20,$2b,$18,$2b
	.byte $40,$78,$29,$00,$00,$ff,$08,$00,$14,$00,$01,$ff,$80,$7e,$00,$00
	.byte $00,$00,$00,$00,$00,$00,$00,$00,$00,$00,$00,$00,$00,$00,$00,$00
VicInit26:
	.byte 8
VicInit25:
	.byte 9
	.fill 4, 0
VicInit2e:
	.byte 10
VicInit27:
	.byte 11
	.fill 8, 0

* = $4000 "Top 1 Even Screen RAM" // 16 lines, odd ones unused
* = $4280 "Top 1 Even Underlay 1-5"
* = $43c0 "Patch NTSC Data"
InitDataValues_NTSC:
	.byte $30,$c0,$0f,$fe,$32,$fe,$32,$2a,$fe,$ff,$2c,$31,$b6,$a9,$c1,$8c
	.byte $17,$d0,$be,$d5,$c0,$0f,$33,$d5,$b4,$fc,$04,$04,$04,$01
* = $43f8 "Top 1 Even Sprite Pointers"
	.byte $e0,$0a,$0b,$0c,$0d,$0e,$d0,$d8
* = $4400 "Top 2 Even Screen RAM" // 16 lines, odd ones unused
* = $4680 "Top 2 Even Underlay 1-5"
* = $47c0 "Patch PAL Data"
InitDataValues_PAL:
	.byte $30,$00,$10,$00,$33,$00,$33,$28,$be,$bf,$8d,$31,$29,$09,$09,$8d
	.byte $bc,$19,$8c,$24,$00,$10,$33,$00,$a4,$3a,$06,$06,$06,$03
* = $47f8 "Top 2 Even Sprite Pointers"
	.byte $e1,$1a,$1b,$1c,$1d,$1e,$d1,$d9
* = $4800 "Top 3 Even Screen RAM" // 16 lines, odd ones unused
* = $4a80 "Top 3 Even Underlay 1-5"
* = $4bc0 "Patch Offsets"
InitDataOffsets:
	.byte $00,$1c,$20,$40,$41,$a8,$a9,$af,$d3,$d8,$e0,$00,$3c,$48,$a5,$a7
	.byte $a8,$a9,$ac,$d2,$de,$df,$00,$05,$28,$29,$48,$49,$4a,$4b
* = $4bf8 "Top 3 Even Sprite Pointers"
	.byte $e2,$2a,$2b,$2c,$2d,$2e,$d2,$da
* = $4c00 "Top 4 Even Screen RAM" // 16 lines, odd ones unused
* = $4e80 "Top 4 Even Underlay 1-5"
* = $4ff8 "Top 4 Even Sprite Pointers"
	.byte $e3,$3a,$3b,$3c,$3d,$3e,$d3,$db
* = $5000 "Top 1 Odd Screen RAM" // 16 lines, even ones unused
* = $5280 "Top 1 Odd Underlay 1-5"
* = $53f8 "Top 1 Odd Sprite Pointers"
	.byte $e4,$4a,$4b,$4c,$4d,$4e,$d4,$dc
* = $5400 "Top 2 Odd Screen RAM" // 16 lines, even ones unused
* = $5680 "Top 2 Odd Underlay 1-5"
* = $57f8 "Top 2 Odd Sprite Pointers"
	.byte $e5,$5a,$5b,$5c,$5d,$5e,$d5,$dd
* = $5800 "Top 3 Odd Screen RAM" // 16 lines, even ones unused
* = $5a80 "Top 3 Odd Underlay 1-5"
* = $5bf8 "Top 3 Odd Sprite Pointers"
	.byte $e6,$6a,$6b,$6c,$6d,$6e,$d6,$de
* = $5c00 "Top 4 Odd Screen RAM" // 16 lines, even ones unused
* = $5e80 "Top 4 Odd Underlay 1-5"
* = $5ff8 "Top 4 Odd Sprite Pointers"
	.byte $e7,$7a,$7b,$7c,$7d,$7e,$d7,$df
* = $6000 "Top Bitmap"
	.fill $1400, 0
.label SharedBitmapPart = *-40
* = $7400 "Top Underlay 6"
* = $7600 "Top Bug Sprite 7"
* = $7800 "Top Bug Sprite 0"

* = $79ff "End"
	.byte 0