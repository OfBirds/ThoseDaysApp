# ThoseDaysApp - Menstrual Cycle Tracker

A progressive web app for tracking and predicting menstrual cycles using statistical averaging. Built with .NET 10, React 18, TypeScript, and PostgreSQL.

## Features

- **Track Cycles**: Log period start dates and duration
- **Predict Future Periods**: Generates 15-cycle predictions based on average interval
- **Calendar View**: Visual calendar with color-coded period marks, a marker for
  today, a countdown on the next period's first day, and a "return to current
  month" button
- **Statistics**: Average cycle length/interval, plus a "next period in N days"
  readout in the status bar
- **Offline Support**: Works offline with cached data (PWA)
- **Responsive Design**: Mobile-friendly design with touch support
- **Light & Dark Themes**: Theme toggle; palette driven by CSS variables
- **Authentication**: Basic auth with password hashing
- **WCAG AA Compliant**: Accessible with proper color contrast and icons

## Tech Stack

- **Backend**: .NET 10 with ASP.NET Core, Entity Framework Core, PostgreSQL
- **Frontend**: React 18, Vite 5, TypeScript 5, CSS3
- **Database**: PostgreSQL 15
- **Containerization**: Docker & Docker Compose

## Getting Started

### Prerequisites

- Docker & Docker Compose
- .NET 10 SDK
- Node.js 18+
- npm or yarn

### Installation

1. **Clone the repository**
```bash
git clone <repo-url>
cd ThoseDaysApp
```

2. **Start PostgreSQL**
```bash
docker-compose up -d
```

3. **Restore backend dependencies**
```bash
dotnet restore ./backend/Api
```

4. **Apply database migrations**
```bash
dotnet ef database update --project backend/Api
```

5. **Install frontend dependencies**
```bash
npm install --prefix ./frontend
```

## Running the Application

### Development Mode

**Terminal 1: Start the backend**
```bash
dotnet watch run --project backend/Api
```
The API will be available at `http://localhost:5200` or `https://localhost:7241`

**Terminal 2: Start the frontend**
```bash
npm run dev --prefix frontend
```
The app will be available at `http://localhost:3000`

### Production Build

```bash
# Build backend
dotnet publish backend/Api -c Release

# Build frontend
npm run build --prefix frontend
```

## API Endpoints

### Authentication
- `POST /api/auth/register` - Create a new account
- `POST /api/auth/login` - Login to account

### Cycles
- `GET /api/user/{userId}/cycles` - Get all cycles for user
- `POST /api/user/{userId}/cycles` - Add new cycle
- `PUT /api/user/{userId}/cycles/{cycleId}` - Edit cycle
- `DELETE /api/user/{userId}/cycles/{cycleId}` - Delete cycle

### Predictions & Statistics
- `POST /api/user/{userId}/predict?cycles=15` - Generate predictions
- `GET /api/user/{userId}/stats` - Get statistics
- `POST /api/user/{userId}/toggle` - Toggle predictions

## Database Schema

### Users Table
- `id` (UUID) - Primary key
- `email` (text) - Unique email
- `password_hash` (text) - Hashed password
- `is_active` (bool) - Account status
- `created_at` (timestamp) - Account creation date

### Cycles Table
- `id` (UUID) - Primary key
- `user_id` (UUID) - Foreign key to Users
- `start_date` (date) - Period start date
- `duration_days` (int) - Period duration
- `created_at` (timestamp) - Record creation date
- `corrected` (bool) - Whether user has corrected this cycle

### Predictions Table
- `id` (UUID) - Primary key
- `user_id` (UUID) - Foreign key to Users
- `predicted_start` (date) - Predicted start date
- `predicted_duration` (int) - Predicted duration
- `confidence` (float) - Confidence score
- `created_at` (timestamp) - Prediction generation date

## Calculation Logic

1. **Cycle Length** = Days from start of cycle N to start of cycle N+1
2. **User Average** = Mean of last N intervals (minimum 1 cycle, default 28 days if only 1)
3. **Prediction** = Last cycle start + rounded user average
4. **On Edit** = Recalculate averages and regenerate next 15 predictions

## Color Scheme

Period marks (red is reserved for real/saved data; predictions go orange → khaki):

- **Actual / saved period**: red (`--period-actual`, #D32F2F light / #FF6B6B dark)
- **Next predicted period**: orange (`--pred-next`, #EF6C00 light / #FFB86B dark)
- **Future predicted periods**: khaki/gold (`--pred-later`, #B8860B light / #FFD966 dark)
- **Today**: accent ring (`--accent`, #FFB86B, constant across themes)
- **Warning** (out-of-range input): khaki (`--warning`); **Error**: red (`--error`)

Brand/surface colors are CSS variables in `frontend/src/styles/index.css` with a
`[data-theme='dark']` override, so both themes share one source of truth.

## Fonts

- **UI**: Inter (sans-serif)
- **Headings**: Merriweather (serif)

## Accessibility

- WCAG AA color contrast compliance
- Both color and icons used for differentiation
- Semantic HTML structure
- Screen reader friendly

## PWA Features

- Installable on mobile and desktop
- Offline support with service worker caching
- Add to home screen capability
- Works without internet connection

## Development Notes

- Uses clean architecture with dependency injection
- Async/await throughout
- EF Core migrations for database management
- TypeScript strict mode enabled
- No external state management library (uses React Context)

## License

MIT

## Support

For issues or questions, please open an issue on the GitHub repository.