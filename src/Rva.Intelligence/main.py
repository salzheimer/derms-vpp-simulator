"""
Rva.Intelligence: AI Sidecar for Resilient VPP Aggregator
Provides LSTM-based load forecasting and energy dispatch optimization.

Architecture:
- FastAPI server for REST API endpoints
- MQTT connection for real-time telemetry consumption
- Redis cache for model state and predictions
- PyTorch-based LSTM models for time-series forecasting

Principle: "The AI Sidecar is a Consultant, not the Decision Maker"
The .NET backend is the source of truth; this service provides recommendations only.
"""

import os
import json
import logging
from datetime import datetime
from contextlib import asynccontextmanager
from typing import Optional

from fastapi import FastAPI, HTTPException, BackgroundTasks
from fastapi.responses import JSONResponse
from pydantic import BaseModel, Field
from pydantic_settings import BaseSettings
import httpx

# Configure logging with JSON format for structured observability
logging.basicConfig(
    level=os.getenv("LOG_LEVEL", "INFO"),
    format="%(asctime)s - %(name)s - %(levelname)s - %(message)s"
)
logger = logging.getLogger(__name__)


# ============================================================================
# Configuration
# ============================================================================

class Settings(BaseSettings):
    """Application configuration with defaults for dev/prod."""
    
    app_name: str = "Rva.Intelligence"
    app_version: str = "0.1.0"
    environment: str = Field(default="dev", alias="ENVIRONMENT")
    log_level: str = Field(default="INFO", alias="LOG_LEVEL")
    
    backend_url: str = Field(default="http://vpp-backend:8080", alias="BACKEND_URL")
    
    # MQTT Settings
    mqtt_host: str = Field(default="mosquitto", alias="MQTT_HOST")
    mqtt_port: int = Field(default=1883, alias="MQTT_PORT")
    mqtt_topic_telemetry: str = "vpp/telemetry/+"
    
    # Redis Settings
    redis_url: str = Field(default="redis://redis:6379", alias="REDIS_URL")
    
    # Model Settings
    forecast_horizon_minutes: int = 60
    lstm_lookback_window: int = 24  # 24-hour history
    
    class Config:
        env_file = ".env"
        case_sensitive = False


settings = Settings()


# ============================================================================
# Request/Response Models
# ============================================================================

class ForecastRequest(BaseModel):
    """Request model for load forecasting."""
    
    substation_id: str = Field(..., description="Substation identifier")
    num_batteries: int = Field(..., ge=1, le=500, description="Number of DERs in substation")
    historical_soc: list[float] = Field(
        ...,
        description="Historical state of charge readings (past 24 hours)"
    )
    timestamp: datetime = Field(default_factory=datetime.utcnow)


class ForecastResponse(BaseModel):
    """Response model for load forecast results."""
    
    forecast_id: str = Field(..., description="Unique forecast ID")
    substation_id: str
    timestamp: datetime
    forecast_horizon_minutes: int
    predicted_demand_kw: list[float] = Field(
        ...,
        description="Predicted load demand at each time step"
    )
    confidence_interval: float = Field(
        default=0.95,
        description="Confidence level for predictions (0.95 = 95%)"
    )
    recommendation: Optional[str] = Field(
        None,
        description="Dispatch recommendation (informational only)"
    )
    is_confident: bool = Field(
        default=True,
        description="Whether the forecast exceeds confidence threshold"
    )


class HealthResponse(BaseModel):
    """Response model for health check."""
    
    status: str
    timestamp: datetime
    version: str
    uptime_seconds: float


# ============================================================================
# Application State & Lifecycle
# ============================================================================

class AppState:
    """Application state management."""
    
    def __init__(self):
        self.start_time = datetime.utcnow()
        self.http_client: Optional[httpx.AsyncClient] = None
        self.models_loaded = False
    
    @property
    def uptime_seconds(self) -> float:
        return (datetime.utcnow() - self.start_time).total_seconds()


app_state = AppState()


@asynccontextmanager
async def lifespan(app: FastAPI):
    """
    Manage application lifecycle: startup and shutdown.
    """
    # Startup
    logger.info(f"Starting {settings.app_name} v{settings.app_version}")
    app_state.http_client = httpx.AsyncClient(base_url=settings.backend_url, timeout=30.0)
    
    # TODO: Load LSTM models from storage or train them
    logger.info("Placeholder: Load LSTM models from disk or train")
    app_state.models_loaded = True
    
    # TODO: Connect to Redis for caching
    logger.info("Placeholder: Connect to Redis cache")
    
    # TODO: Subscribe to MQTT topics for real-time telemetry
    logger.info("Placeholder: Subscribe to MQTT telemetry topics")
    
    yield
    
    # Shutdown
    logger.info("Shutting down AI Sidecar")
    if app_state.http_client:
        await app_state.http_client.aclose()
    
    # TODO: Cleanup connections (Redis, MQTT, etc.)
    logger.info("Cleanup complete")


# ============================================================================
# FastAPI Application
# ============================================================================

app = FastAPI(
    title=settings.app_name,
    version=settings.app_version,
    description="AI Sidecar for Resilient VPP Aggregator",
    lifespan=lifespan
)


# ============================================================================
# Endpoints
# ============================================================================

@app.get("/health", response_model=HealthResponse, tags=["Health"])
async def health_check():
    """
    Health check endpoint for orchestration and monitoring.
    """
    return HealthResponse(
        status="healthy" if app_state.models_loaded else "loading",
        timestamp=datetime.utcnow(),
        version=settings.app_version,
        uptime_seconds=app_state.uptime_seconds
    )


@app.post("/forecast", response_model=ForecastResponse, tags=["Forecasting"])
async def forecast_load(request: ForecastRequest):
    """
    Generate LSTM-based load forecast for a substation.
    
    Args:
        request: Forecast request with historical SOC data and substation info
        
    Returns:
        ForecastResponse with predicted demand and dispatch recommendation
        
    Note:
        This endpoint returns a forecast only—it does NOT command dispatch.
        The .NET backend evaluates this recommendation against grid constraints.
    """
    try:
        if not app_state.models_loaded:
            raise HTTPException(status_code=503, detail="Models not yet loaded")
        
        logger.info(
            f"Forecasting for substation {request.substation_id} "
            f"with {request.num_batteries} batteries"
        )
        
        # TODO: Invoke LSTM model with request.historical_soc
        # Placeholder: Return mock forecast
        import uuid
        forecast_id = str(uuid.uuid4())
        
        # Simulate LSTM output: predict next 60 minutes of load
        predicted_demand = [50.0 + (i % 10 - 5) for i in range(60)]
        
        recommendation = (
            "Dispatch 40% of fleet to absorb baseline load; "
            "hold 30% in reserve for grid support"
        )
        
        return ForecastResponse(
            forecast_id=forecast_id,
            substation_id=request.substation_id,
            timestamp=datetime.utcnow(),
            forecast_horizon_minutes=settings.forecast_horizon_minutes,
            predicted_demand_kw=predicted_demand,
            confidence_interval=0.95,
            recommendation=recommendation,
            is_confident=True
        )
    
    except Exception as e:
        logger.error(f"Forecast failed: {str(e)}", exc_info=True)
        raise HTTPException(status_code=500, detail=str(e))


@app.get(
    "/status",
    tags=["Status"],
    responses={
        200: {"description": "Sidecar operational status"}
    }
)
async def status():
    """
    Retrieve current operational status of the AI Sidecar.
    """
    return {
        "service": settings.app_name,
        "version": settings.app_version,
        "uptime_seconds": app_state.uptime_seconds,
        "models_loaded": app_state.models_loaded,
        "environment": settings.environment,
        "timestamp": datetime.utcnow().isoformat()
    }


# ============================================================================
# Health Probes
# ============================================================================

@app.get("/ready", tags=["Probes"])
async def ready_probe():
    """
    Readiness probe: returns 200 only when ready to accept requests.
    """
    if not app_state.models_loaded:
        return JSONResponse(
            status_code=503,
            content={"ready": False, "reason": "Models still loading"}
        )
    return JSONResponse(status_code=200, content={"ready": True})


@app.get("/live", tags=["Probes"])
async def live_probe():
    """
    Liveness probe: returns 200 if service is alive.
    """
    return JSONResponse(status_code=200, content={"alive": True})


# ============================================================================
# Entry Point
# ============================================================================

if __name__ == "__main__":
    import uvicorn
    
    uvicorn.run(
        app,
        host="0.0.0.0",
        port=8000,
        log_level=settings.log_level.lower()
    )
