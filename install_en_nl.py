import argostranslate.package

# Update package index to get the list of available translations
argostranslate.package.update_package_index()

# Get available translation packages
pkgs = argostranslate.package.get_available_packages()

# Try to find English to Dutch package and install it
for p in pkgs:
    if p.from_code == "en" and p.to_code == "nl":
        print("Found package:", p)
        path = p.download()
        argostranslate.package.install_from_path(path)
        print("Installed:", p)
        break
else:
    print("No en->nl package found in index.")
