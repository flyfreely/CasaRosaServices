rem use docker compose, port is 8237
rem docker run --rm -it -p 5000:5000 emailchecker
docker compose run --rm --service-ports -it app
