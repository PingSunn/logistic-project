.PHONY: run build test run-test

# รัน GUI ปกติ
run:
	dotnet run --project CargoFit/CargoFit.csproj

# Build เฉยๆ
build:
	dotnet build

# รัน tests ทั้งหมด
test:
	dotnet test CargoFit.Tests/CargoFit.Tests.csproj --logger "console;verbosity=detailed"

# รัน packing engine แบบ headless ด้วย JSON fixture
# Usage: make run-test FILE=testdata/devpreset.json
run-test:
ifndef FILE
	$(error กรุณาระบุ FILE เช่น: make run-test FILE=testdata/devpreset.json)
endif
	dotnet run --project CargoFit/CargoFit.csproj -- --input $(FILE)
