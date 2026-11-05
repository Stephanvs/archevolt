Tier-1 x64 listings captured at b882d90 with DOTNET_JitDisasm under BenchmarkDotNet --inProcess --job Short (default tiered PGO).

## Generated [Each] movement loop (Movement+__Archevolt_EachBody:Run, Tier1, row loop)

```asm
G_M000_IG07:                ;; offset=0x020C
       lea      r15, bword ptr [rsi+r14]
       lea      r13, bword ptr [rdi+r14]
       vmovss   xmm0, dword ptr [rax+0x30]
       vmulss   xmm1, xmm0, dword ptr [r13]
       vaddss   xmm1, xmm1, dword ptr [r15]
       vmovss   xmm2, dword ptr [r15+0x04]
       vmulss   xmm3, xmm0, dword ptr [r13+0x04]
       vaddss   xmm2, xmm2, xmm3
       vmovss   xmm3, dword ptr [r15+0x08]
       vmulss   xmm0, xmm0, dword ptr [r13+0x08]
       vaddss   xmm0, xmm3, xmm0
       vmovss   dword ptr [r15], xmm1
       vmovss   dword ptr [r15+0x04], xmm2
       vmovss   dword ptr [r15+0x08], xmm0
       add      r14, 12
       dec      ebp
       jne      SHORT G_M000_IG07
```

## Handwritten loop (MovementBenchmarks:Handwritten, Tier1, row loop)

```asm
G_M000_IG04:                ;; offset=0x0022
       lea      r10, bword ptr [rax+r8]
       lea      r9, bword ptr [rcx+r8]
       vmulss   xmm1, xmm0, dword ptr [r9]
       vaddss   xmm1, xmm1, dword ptr [r10]
       vmovss   xmm2, dword ptr [r10+0x04]
       vmulss   xmm3, xmm0, dword ptr [r9+0x04]
       vaddss   xmm2, xmm2, xmm3
       vmovss   xmm3, dword ptr [r10+0x08]
       vmulss   xmm4, xmm0, dword ptr [r9+0x08]
       vaddss   xmm3, xmm3, xmm4
       vmovss   dword ptr [r10], xmm1
       vmovss   dword ptr [r10+0x04], xmm2
       vmovss   dword ptr [r10+0x08], xmm3
       add      r8, 12
       dec      edx
       jne      SHORT G_M000_IG04
```

## Tracked [Each] write (MatrixSystems+__Archevolt_MarkBody:Run, Tier1, row loop)

```asm
G_M000_IG08:                ;; offset=0x0222
       mov      r10d, ebx
       mov      qword ptr [rdi+8*r10], rax
       lea      r10, [r10+2*r10]
       shl      r10, 2
       lea      rsi, bword ptr [r14+r10]
       add      r10, r9
       vmulss   xmm1, xmm0, dword ptr [r10]
       vaddss   xmm1, xmm1, dword ptr [rsi]
       vmovss   xmm2, dword ptr [rsi+0x04]
       vmulss   xmm3, xmm0, dword ptr [r10+0x04]
       vaddss   xmm2, xmm2, xmm3
       vmovss   xmm3, dword ptr [rsi+0x08]
       vmulss   xmm4, xmm0, dword ptr [r10+0x08]
       vaddss   xmm3, xmm3, xmm4
       vmovss   dword ptr [rsi], xmm1
       vmovss   dword ptr [rsi+0x04], xmm2
       vmovss   dword ptr [rsi+0x08], xmm3
       inc      ebx
       cmp      ebx, r11d
       jl       SHORT G_M000_IG08
```

## Changed<T> named-query scan (MatrixSystems+__Archevolt_ChangedBody:Run, Tier1, row loop)

```asm
G_M000_IG05:                ;; offset=0x02AD
       mov      r15, qword ptr [r14]
       mov      rdx, qword ptr [rsp+0x40]
       cmp      r15, rdx
       jbe      G_M000_IG18
       mov      esi, ecx
       mov      rcx, r15
       mov      r15d, esi
       cmp      rcx, rbx
       ja       SHORT G_M000_IG06
       inc      rdi
       vcvtss2sd xmm0, xmm0, dword ptr [rbp]
       vaddsd   xmm6, xmm6, xmm0
G_M000_IG06:                ;; offset=0x02D7
       add      rbp, 12
       add      r14, 8
       lea      ecx, [r15-0x01]
       test     r15d, r15d
       jne      SHORT G_M000_IG05
```

## World.TryGet<Position> (Tier1, hot path to the first return)

```asm
G_M000_IG02:                ;; offset=0x0005
       mov      rax, qword ptr GS:[0x0058]
       mov      rax, qword ptr [rax+0x48]
       add      rax, 608
       mov      eax, dword ptr [rax+0x10]
       cmp      eax, dword ptr [rcx+0x78]
       jne      G_M000_IG05
       cmp      byte  ptr [rcx+0x80], 0
       jne      G_M000_IG06
       cmp      byte  ptr [rcx+0x81], 0
       jne      G_M000_IG07
       xor      eax, eax
       mov      qword ptr [r8], rax
       mov      dword ptr [r8+0x08], eax
       mov      rax, gword ptr [rcx+0x48]
       mov      r10, qword ptr [rdx]
       mov      r9d, dword ptr [rdx+0x08]
       mov      edx, dword ptr [rdx+0x0C]
       mov      r11, qword ptr [rcx+0x68]
       cmp      byte  ptr [rax], al
       cmp      r10, r11
       jne      G_M000_IG08
       cmp      r9d, dword ptr [rax+0x18]
       jae      G_M000_IG08
       mov      rax, gword ptr [rax+0x08]
       mov      r10d, r9d
       shr      r10d, 10
       cmp      r10d, dword ptr [rax+0x08]
       jae      G_M000_IG14
       mov      rax, gword ptr [rax+8*r10+0x10]
       and      r9d, 0x3FF
       cmp      r9d, dword ptr [rax+0x08]
       jae      G_M000_IG14
       mov      r10d, r9d
       shl      r10, 4
       lea      rax, bword ptr [rax+r10+0x10]
       mov      r10d, dword ptr [rax+0x08]
       mov      r9d, dword ptr [rax+0x0C]
       cmp      dword ptr [rax], edx
       jne      G_M000_IG08
       cmp      byte  ptr [rax+0x04], 2
       jne      G_M000_IG08
       mov      rax, gword ptr [rcx+0x40]
       mov      rdx, rax
       mov      rdx, gword ptr [rdx+0x10]
       cmp      dword ptr [rdx+0x08], 6
       jle      G_M000_IG10
       cmp      dword ptr [rdx+0x08], 6
G_M000_IG03:                ;; offset=0x00D8
       jbe      G_M000_IG14
       mov      edx, dword ptr [rdx+0x28]
       test     edx, edx
       jl       G_M000_IG10
       mov      rax, gword ptr [rax+0x08]
       cmp      edx, dword ptr [rax+0x08]
       jae      G_M000_IG14
       mov      r11d, edx
       mov      rax, gword ptr [rax+8*r11+0x10]
       cmp      byte  ptr [rax+0x21], 0
       jne      G_M000_IG11
       mov      rax, gword ptr [rcx+0x50]
       cmp      r10d, dword ptr [rax+0x10]
       jae      G_M000_IG12
       mov      rax, gword ptr [rax+0x08]
       cmp      r10d, dword ptr [rax+0x08]
       jae      G_M000_IG14
       mov      ecx, r10d
       mov      rax, gword ptr [rax+8*rcx+0x10]
       mov      rcx, gword ptr [rax+0x28]
       cmp      edx, dword ptr [rcx+0x08]
       jae      G_M000_IG14
       mov      ecx, dword ptr [rcx+4*rdx+0x10]
       test     ecx, ecx
       jl       G_M000_IG08
       mov      rax, gword ptr [rax+0x20]
       mov      rdx, gword ptr [rax+0x08]
       cmp      ecx, dword ptr [rdx+0x08]
       jae      G_M000_IG14
       mov      rcx, qword ptr [rdx+8*rcx+0x10]
       mov      eax, dword ptr [rax+0x50]
       test     eax, eax
       jl       G_M000_IG13
       cmp      r9d, eax
       jae      G_M000_IG14
       mov      eax, r9d
       lea      rax, [rax+2*rax]
       mov      rdx, qword ptr [rcx+4*rax]
       mov      qword ptr [r8], rdx
       mov      edx, dword ptr [rcx+4*rax+0x08]
       mov      dword ptr [r8+0x08], edx
       mov      eax, 1
```
