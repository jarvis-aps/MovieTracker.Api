import { useEffect, useState } from 'react'
import './App.css'

const API_URL = 'http://localhost:5066'

enum MovieStatus {
  Watched = 0,
  NotWatched = 1,
}

interface Movie {
  id: number
  title: string
  status: MovieStatus
}

function App() {
  const [movies, setMovies] = useState<Movie[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    fetch(`${API_URL}/movies`)
      .then((res) => res.json())
      .then((data: Movie[]) => setMovies(data))
      .catch(() => setError('Не удалось загрузить список фильмов'))
      .finally(() => setLoading(false))
  }, [])

  async function toggleStatus(movie: Movie) {
    const newStatus =
      movie.status === MovieStatus.Watched
        ? MovieStatus.NotWatched
        : MovieStatus.Watched

    const response = await fetch(
      `${API_URL}/movies/${movie.id}/status?status=${MovieStatus[newStatus]}`,
      { method: 'PATCH' },
    )

    if (!response.ok) {
      setError(`Не удалось обновить статус фильма "${movie.title}"`)
      return
    }

    setMovies((prev) =>
      prev.map((m) => (m.id === movie.id ? { ...m, status: newStatus } : m)),
    )
  }

  if (loading) return <p>Загрузка…</p>
  if (error) return <p>{error}</p>

  return (
    <main>
      <h1>MovieTracker</h1>
      <ul>
        {movies.map((movie) => (
          <li key={movie.id}>
            <label>
              <input
                type="checkbox"
                checked={movie.status === MovieStatus.Watched}
                onChange={() => toggleStatus(movie)}
              />
              {movie.title} — {MovieStatus[movie.status]}
            </label>
          </li>
        ))}
      </ul>
    </main>
  )
}

export default App
