# Module Dependencies

Which projects may reference which. Anything not shown is prohibited, and `tests/Rva.ArchitectureTests` fails the build on a violation.

```mermaid
flowchart TB
    HOST["Rva.Host<br/>composition root"]

    subgraph IMPL["Module implementations (internal)"]
        direction LR
        GT["Rva.Modules.GridTopology"]
        TE["Rva.Modules.Telemetry"]
        SI["Rva.Modules.Simulation"]
    end

    subgraph CON["Module contracts (public API)"]
        direction LR
        GTC["Rva.Modules.GridTopology.Contracts"]
        TEC["Rva.Modules.Telemetry.Contracts"]
        SIC["Rva.Modules.Simulation.Contracts"]
    end

    SK["Rva.SharedKernel<br/>typed IDs, units, reference codes, Result"]

    HOST --> IMPL
    HOST --> CON
    HOST --> SK

    GT --> GTC
    TE --> TEC
    SI --> SIC

    TE -->|"asset events for known_assets"| GTC
    SI -->|"validate assets in CreateRun"| GTC

    GTC --> SK
    TEC --> SK
    SIC --> SK
    IMPL --> SK
```

## Reference rules

| Project | May reference |
|---|---|
| `Rva.Host` | All modules and their `.Contracts`, `Rva.SharedKernel` |
| `Rva.Modules.<X>` | `Rva.SharedKernel`, its own `.Contracts`, other modules' `.Contracts` |
| `Rva.Modules.<X>.Contracts` | `Rva.SharedKernel` only |
| `Rva.SharedKernel` | Nothing outside the .NET base class library |

- **Module implementations never reference each other.** A module that needs another module's data uses that module's `.Contracts`.
- **The cross-module arrows above are the Phase 1 dependencies:** Telemetry and Simulation both depend on GridTopology's contracts. Later modules (for example Dispatch) follow the same pattern.
- **`Rva.Simulator` and `Rva.Intelligence` reference no backend project.** They talk to the backend only over HTTP and MQTT.
