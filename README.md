# DailyTrainTimetable

DailyTrainTimetable is a .NET 8 Console App that fetches Taiwan Railway daily train timetable data from TDX and publishes a small static JSON dataset for app clients.

GitHub Actions only downloads and lightly normalizes the original daily timetable data. The app side can download these JSON files and convert them into SQLite tables for local query.

## TDX API

This project uses:

```text
https://tdx.transportdata.tw/api/basic/v3/Rail/TRA/DailyTrainTimetable/TrainDate/{yyyy-MM-dd}
```

Example:

```text
https://tdx.transportdata.tw/api/basic/v3/Rail/TRA/DailyTrainTimetable/TrainDate/2026-04-13
```

TDX authentication uses the OAuth client credentials flow. The app reads credentials from environment variables:

```text
TDX_CLIENT_ID
TDX_CLIENT_SECRET
```

## Local Run

Install .NET 8 SDK, then set credentials and run:

```powershell
$env:TDX_CLIENT_ID = "your-client-id"
$env:TDX_CLIENT_SECRET = "your-client-secret"
dotnet run
```

By default, the app generates data for today plus the next 6 days, using the `Asia/Taipei` date.

To change the number of days:

```powershell
dotnet run -- --days 14
```

## Generated Files

Files are written to `output/data`.

```text
output/data/{yyyyMMdd}.json
output/data/latest.json
output/data/stations.json
```

`{yyyyMMdd}.json` contains one date of train timetable data:

```json
{
  "trainDate": "2026-04-13",
  "source": "TDX",
  "updatedAt": "2026-06-08T05:00:00+08:00",
  "trainTimetables": [
    {
      "trainNo": "123",
      "direction": 0,
      "trainTypeId": "1100",
      "trainTypeCode": "1",
      "trainTypeName": "自強",
      "startingStationId": "1000",
      "startingStationName": "臺北",
      "endingStationId": "1025",
      "endingStationName": "新竹",
      "stopTimes": [
        {
          "stationId": "1000",
          "stationName": "臺北",
          "arrivalTime": "08:00",
          "departureTime": "08:02",
          "stopSequence": 1
        }
      ]
    }
  ]
}
```

`latest.json` contains:

- `updatedAt`: generation time in Taiwan time
- `availableDates`: dates that were successfully generated
- `dataVersion`: static schema/data version

`stations.json` contains stations found in successful timetable files:

- `stationId`
- `stationName`

If a single date fails, the app logs the error and skips that date. `latest.json` only lists successfully generated dates. Empty API responses are logged clearly.

## GitHub Secrets

In your GitHub repository:

1. Open `Settings`.
2. Open `Secrets and variables` > `Actions`.
3. Add repository secrets:
   - `TDX_CLIENT_ID`
   - `TDX_CLIENT_SECRET`

## GitHub Pages

The workflow publishes the `output` directory to GitHub Pages.

To enable Pages:

1. Open `Settings` > `Pages`.
2. Under `Build and deployment`, set `Source` to `GitHub Actions`.
3. Run the `Update train data` workflow manually once, or wait for the schedule.

The workflow runs every day at Taiwan time 05:00 and also supports manual `workflow_dispatch`.

## App SQLite Tables

The app side can convert downloaded JSON into two SQLite tables.

### TrainDailyTimetable

```sql
CREATE TABLE TrainDailyTimetable (
    TrainDate TEXT NOT NULL,
    TrainNo TEXT NOT NULL,
    Direction INTEGER,
    TrainTypeCode TEXT,
    TrainTypeName TEXT,
    StartingStationId TEXT,
    EndingStationId TEXT,
    PRIMARY KEY (TrainDate, TrainNo)
);
```

### TrainStopTime

```sql
CREATE TABLE TrainStopTime (
    TrainDate TEXT NOT NULL,
    TrainNo TEXT NOT NULL,
    StationId TEXT NOT NULL,
    StationName TEXT,
    ArrivalTime TEXT,
    DepartureTime TEXT,
    StopSequence INTEGER,
    PRIMARY KEY (TrainDate, TrainNo, StationId, StopSequence)
);
```

Suggested indexes:

```sql
CREATE INDEX IX_TrainStopTime_Search
ON TrainStopTime (TrainDate, StationId, DepartureTime, StopSequence);

CREATE INDEX IX_TrainStopTime_Train
ON TrainStopTime (TrainDate, TrainNo, StopSequence);
```

## Query Logic Example

For the same `TrainDate` and `TrainNo`:

- departure station `StopSequence` must be less than arrival station `StopSequence`
- departure `DepartureTime` must be greater than or equal to the user selected time

Example:

```sql
SELECT
    t.TrainDate,
    t.TrainNo,
    t.Direction,
    t.TrainTypeCode,
    t.TrainTypeName,
    depart.StationName AS DepartureStationName,
    arrive.StationName AS ArrivalStationName,
    depart.DepartureTime,
    arrive.ArrivalTime
FROM TrainDailyTimetable t
JOIN TrainStopTime depart
    ON depart.TrainDate = t.TrainDate
   AND depart.TrainNo = t.TrainNo
JOIN TrainStopTime arrive
    ON arrive.TrainDate = t.TrainDate
   AND arrive.TrainNo = t.TrainNo
WHERE t.TrainDate = @trainDate
  AND depart.StationId = @departureStationId
  AND arrive.StationId = @arrivalStationId
  AND depart.StopSequence < arrive.StopSequence
  AND depart.DepartureTime >= @departureTime
ORDER BY depart.DepartureTime, arrive.ArrivalTime;
```
