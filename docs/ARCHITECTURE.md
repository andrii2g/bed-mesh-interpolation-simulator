# Architecture

## System purpose

The program models a printer bed as a continuous analytical scalar field:

\[
z=f(x,y)
\]

A virtual probe samples this field at a small number of coordinates. An interpolation algorithm sees **only those samples** and estimates the surface between them.

A separate dense evaluator compares the estimate against the original analytical field.

## Component architecture

```mermaid
flowchart TB
    subgraph Domain
        G[Geometry]
        S[Analytical surfaces]
    end

    subgraph Sampling
        P[ProbeSimulator]
        M[ProbeGrid]
    end

    subgraph Reconstruction
        B[BilinearInterpolator]
        I[InverseDistanceInterpolator]
    end

    subgraph Evaluation
        E[SurfaceEvaluator]
        X[ErrorMetricsCalculator]
    end

    subgraph Presentation
        C[CLI]
        V[SVG renderers]
    end

    S --> P
    G --> P
    P --> M
    M --> B
    M --> I
    S --> E
    B --> E
    I --> E
    E --> X
    E --> V
    M --> V
    X --> C
    V --> C
```

## Dependency rule

```mermaid
flowchart LR
    CLI --> Core
    Tests --> Core
    Core --> BCL[.NET BCL]
```

`BedMesh.Core` must not reference CLI concerns.

## Information boundary

The most important architectural invariant is:

```mermaid
flowchart LR
    A[IBedSurface] -->|sample| B[ProbeGrid]
    B --> C[Interpolator]
    A -. forbidden .-> C
```

The interpolator must never receive the analytical surface.

Otherwise the simulator would accidentally leak ground truth into reconstruction.

## Main runtime sequence

```mermaid
sequenceDiagram
    participant CLI
    participant Scenario
    participant Probe as ProbeSimulator
    participant Interpolator
    participant Evaluator
    participant Metrics
    participant SVG

    CLI->>Scenario: Create analytical bed
    CLI->>Probe: Sample surface at N x N
    Probe-->>CLI: ProbeGrid
    CLI->>Evaluator: Evaluate dense grid
    loop each dense coordinate
        Evaluator->>Scenario: Get true Z
        Evaluator->>Interpolator: Estimate Z from ProbeGrid
    end
    Evaluator-->>CLI: SurfaceEvaluation
    CLI->>Metrics: Calculate errors
    Metrics-->>CLI: ErrorMetrics
    CLI->>SVG: Render artifacts
    SVG-->>CLI: SVG files
```

## Core abstractions

### `IBedSurface`

Represents ground truth.

```csharp
public interface IBedSurface
{
    double GetHeight(double x, double y);
}
```

### `ProbeGrid`

Represents exactly what the printer has measured.

It contains:

- physical X/Y coordinate;
- measured Z;
- logical row/column index.

### `ISurfaceInterpolator`

Consumes only `ProbeGrid`.

```csharp
public interface ISurfaceInterpolator
{
    string Name { get; }
    double Interpolate(ProbeGrid grid, double x, double y);
}
```

### `SurfaceEvaluator`

Creates dense truth/reconstruction/error samples.

## Data flow

```mermaid
flowchart LR
    A[Scenario parameters]
    A --> B[Analytical surface]
    B --> C[Sparse probing]
    C --> D[ProbeGrid]
    D --> E[Interpolation]
    B --> F[Dense truth sampling]
    E --> G[Dense reconstruction]
    F --> H[Pointwise subtraction]
    G --> H
    H --> I[Metrics]
    H --> J[Error heatmaps]
```

## Error convention

\[
e=\hat z-z
\]

This convention is mandatory across:

- evaluator;
- metrics;
- console;
- CSV;
- SVG legends;
- documentation.

## Determinism

The MVP has no random data.

If probe noise is added later:

```mermaid
flowchart LR
    Seed --> PRNG
    PRNG --> Noise
    Noise --> ProbeSamples
```

A seed must be mandatory or defaulted deterministically.

## Extension points

Safe future extensions:

- probe noise;
- alternative interpolation;
- adaptive mesh experiments;
- imported measured bed data;
- thermal deformation models;
- real probe datasets;
- bicubic interpolation.

Do not couple these extensions to printer firmware.
